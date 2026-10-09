using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Shows a baked 3D resident in place of its sprite. The source SpriteRenderer stays the
    /// logical anchor: the model is visible only while that renderer is enabled and has no sprite
    /// (death swaps in the ghost sprite), and it removes itself when the anchor is destroyed.
    /// Rigged residents play the shared Mixamo clips; static ones get procedural motion: a walking
    /// bob and sway. Both turn to face, lean into attacks and lie on the bed.</summary>
    public sealed class CharacterModel : MonoBehaviour
    {
        const float WalkStep = 9f, AttackSeconds = .35f, Fade = .18f;
        /// <summary>Ground speed in tiles per second at which each clip's feet do not slide, for a
        /// 1.5-tile resident (Mixamo walk ≈ 1.4 m/s and run ≈ 3.6 m/s for a 1.8 m figure).</summary>
        const float WalkSpeed = 1.2f, RunSpeed = 3f, ScaredSpeed = 3.4f;
        static readonly int IdleState = Animator.StringToHash("Base Layer.Idle"), WalkState = Animator.StringToHash("Base Layer.Walk"),
            RunState = Animator.StringToHash("Base Layer.Run"), ScaredState = Animator.StringToHash("Base Layer.RunScared"),
            AttackState = Animator.StringToHash("Base Layer.Attack");
        static Shader shader;
        SpriteRenderer anchor;
        Renderer view;
        Material material;
        Animator animator;
        CharacterKit.Model model;
        float scale, yaw, walk, moveBlend, attackLeft;
        int playing;
        bool lying;
        Vector3 lyingPosition, lyingFrom;
        Quaternion lyingRotation, lyingFromRotation;

        /// <summary>Lift above the floor in tiles (the Night Porter floats).</summary>
        public float Hover;
        /// <summary>Plays the idle in place of walk and run, for bodies that glide (Widow Mildred's gown).</summary>
        public bool Glide;
        int actionState;

        public float Height => model.Height * scale;
        public float HalfDepth => model.HalfDepth * scale;
        public bool Animated => animator != null;

        public static CharacterModel Create(SpriteRenderer anchor, CharacterKit.Model model, float height, Transform parent)
        {
            if (anchor == null || model == null) return null;
            var go = new GameObject(anchor.name + " (3D)");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<CharacterModel>();
            c.anchor = anchor; c.model = model; c.scale = height / Mathf.Max(.01f, model.Height);
            if (model.Rig != null && model.RigHeight > .01f)
            {
                // The rig is in Unity axes (Y up, face +Z); turn it into game axes (-Z up, face +Y).
                var rig = Instantiate(model.Rig, go.transform, false);
                rig.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                rig.transform.localScale = Vector3.one * (model.Height / model.RigHeight);
                c.animator = rig.GetComponent<Animator>();
                c.view = rig.GetComponentInChildren<SkinnedMeshRenderer>();
                c.material = c.view.material; // per-resident instance, destroyed with the model
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
                var mesh = go.AddComponent<MeshRenderer>();
                mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mesh.receiveShadows = false;
                if (shader == null) shader = Resources.Load<Shader>("Shaders/HotelSurface");
                c.material = new Material(shader) { name = "Resident " + model.Id, mainTexture = model.Albedo };
                mesh.sharedMaterial = c.material; c.view = mesh;
            }
            anchor.sprite = null;
            go.transform.localScale = Vector3.one * c.scale;
            return c;
        }

        /// <summary>Standing pose: ground position, facing direction in world XY, ground speed in tiles
        /// per second, whether the resident is fleeing, and whether to start an attack.</summary>
        public void Drive(Vector2 ground, Vector2 facing, float speed, bool scared, bool attack)
        {
            lying = false;
            bool moving = speed > .4f;
            if (facing.sqrMagnitude > 1e-4f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f, 720f * Time.deltaTime);
            moveBlend = Mathf.MoveTowards(moveBlend, moving ? 1f : 0f, Time.deltaTime * 6f);
            if (attack && attackLeft <= 0f) attackLeft = AttackSeconds;
            attackLeft = Mathf.Max(0f, attackLeft - Time.deltaTime);
            float attackLean = Mathf.Sin(attackLeft / AttackSeconds * Mathf.PI) * 16f;

            float bob = 0f, sway = 0f, lean = attackLean;
            if (animator != null) Animate(moving, speed, scared, attack);
            else
            {
                walk += Time.deltaTime * WalkStep * moveBlend;
                bob = Mathf.Abs(Mathf.Sin(walk)) * .06f * moveBlend + Mathf.Sin(Time.time * 2f) * .006f;
                sway = Mathf.Sin(walk) * 5f * moveBlend;
                lean += 6f * moveBlend;
            }
            // Body axes: +Y face, -Z up. Lean tips the head toward the face; sway rolls side to side.
            var body = Quaternion.Euler(0f, 0f, yaw) * Quaternion.Euler(lean, sway, 0f);
            if (Glide) bob = Mathf.Sin(Time.time * 2.4f) * .04f;
            transform.SetPositionAndRotation(new Vector3(ground.x, ground.y, .06f - bob - Hover), body);
        }

        /// <summary>Grows or shrinks the model from its created size (the monster's growth stages).</summary>
        public void SetSize(float multiplier) => transform.localScale = Vector3.one * scale * multiplier;

        /// <summary>Plays a one-shot or held state (Cast, Special, Eat) when the controller has it; the next
        /// Drive returns to locomotion once a one-shot finishes. False when there is no such state.</summary>
        public bool PlayAction(string state)
        {
            if (animator == null) return false;
            int hash = Animator.StringToHash("Base Layer." + state);
            if (!animator.HasState(0, hash)) return false;
            actionState = hash; playing = 0; Play(hash); animator.speed = 1f;
            return true;
        }

        public void StopAction() { actionState = 0; }

        /// <summary>Scales the model's colour (this instance's own material only).</summary>
        public void Brighten(float k) { if (material != null) material.color = new Color(k, k, k, 1f); }

        void Animate(bool moving, float speed, bool scared, bool attack)
        {
            bool canAttack = animator.HasState(0, AttackState);
            if (attack && canAttack && playing != AttackState) { actionState = 0; Play(AttackState); return; }
            if (playing == AttackState && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f) return;
            if (actionState != 0)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.loop || info.normalizedTime < 1f || animator.IsInTransition(0)) return;
                actionState = 0;
            }
            if (Glide) { moving = false; speed = 0f; }
            int want = IdleState; float natural = 1f;
            if (moving)
            {
                if (scared && animator.HasState(0, ScaredState)) { want = ScaredState; natural = ScaredSpeed; }
                else if (speed < (WalkSpeed + RunSpeed) * .5f || !animator.HasState(0, RunState)) { want = WalkState; natural = WalkSpeed; }
                else { want = RunState; natural = RunSpeed; }
            }
            if (!animator.HasState(0, want)) want = IdleState;
            Play(want);
            // Match foot speed to ground speed; never freeze or sprint unnaturally.
            animator.speed = moving ? Mathf.Clamp(speed / natural, .6f, 1.6f) : 1f;
        }

        void Play(int state)
        {
            if (playing == state) return;
            playing = state;
            animator.CrossFadeInFixedTime(state, Fade, 0);
        }

        /// <summary>Lying on the back with the head at <paramref name="head"/>, body along <paramref name="toward"/>,
        /// back resting on a surface at depth <paramref name="surfaceZ"/>. <paramref name="blend"/> eases in from standing.</summary>
        public void Lie(Vector2 head, Vector2 toward, float surfaceZ, float blend)
        {
            var dir = new Vector3(toward.x, toward.y, 0f).normalized;
            lyingRotation = Quaternion.LookRotation(-dir, Vector3.back);
            var feet = (Vector3)head - dir * Height * .92f;
            lyingPosition = new Vector3(feet.x, feet.y, surfaceZ - HalfDepth);
            if (!lying) { transform.GetPositionAndRotation(out var p, out var r); lyingFrom = p; lyingFromRotation = r; lying = true; }
            float k = Mathf.SmoothStep(0f, 1f, blend);
            transform.SetPositionAndRotation(Vector3.Lerp(lyingFrom, lyingPosition, k), Quaternion.Slerp(lyingFromRotation, lyingRotation, k));
            moveBlend = 0f; attackLeft = 0f;
            if (animator != null)
            {
                // The standing idle, laid flat and slowed, reads as quiet breathing in bed.
                Play(IdleState); animator.speed = .35f;
            }
        }

        void LateUpdate()
        {
            if (anchor == null) { Destroy(gameObject); return; }
            bool visible = anchor.enabled && anchor.gameObject.activeInHierarchy && anchor.sprite == null;
            // Hidden rigs stay enabled: the Animator's CullUpdateTransforms mode already skips their
            // bones, while disabling it would snap the skeleton back to the export's rest pose.
            view.enabled = visible;
        }

        void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
