using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using BadAppleHotel.Game;

namespace BadAppleHotel.Tests
{
    public class WallShaderBuildTests
    {
        [Test] public void Android_instancing_layout_matches_submission_capacity_and_minimum_GLES_uniform_block_budget()
        {
            var shader = Resources.Load<Shader>("Shaders/HotelWall");
            Assert.IsNotNull(shader);
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            var compiled = pass.CompileVariant(ShaderType.Vertex, new[] { "INSTANCING_ON" },
                ShaderCompilerPlatform.GLES3x, BuildTarget.Android, GraphicsTier.Tier2, true);
            Assert.IsTrue(compiled.Success, string.Join("\n", compiled.Messages.Select(message => message.message)));
            Assert.IsNotNull(compiled.ConstantBuffers);
            // Inspect Unity's actual Android compiler output. Desktop rendering alone cannot
            // catch a GLES linker failure or a mismatch between shader arrays and draw chunks.
            foreach (string name in new[] { "UnityInstancing_PerDraw0", "UnityInstancing_WallProps" })
            {
                var buffer = compiled.ConstantBuffers.Single(item => item.Name == name);
                Assert.LessOrEqual(buffer.Size, 16384, name + " exceeds the GLES3 minimum uniform-block size.");
                var array = buffer.Fields.Single(field => field.ArraySize > 0);
                Assert.AreEqual(WallInstances.MaxBatchCapacity, array.ArraySize,
                    name + " must agree with the C# draw chunk and property-array size.");
            }
            Assert.AreEqual(WallInstances.MaxBatchCapacity, new WallInstances().BatchCapacity);
            string glsl = System.Text.Encoding.UTF8.GetString(compiled.ShaderData);
            StringAssert.Contains("#define HLSLCC_ENABLE_UNIFORM_BUFFERS 1", glsl,
                "GLES wall arrays must be emitted as uniform blocks, not ordinary vertex uniforms.");
        }

        [Test] public void Local_peek_base_fragment_compiles_on_android_GLES3()
        {
            var shader=Resources.Load<Shader>("Shaders/HotelWall");
            var result=ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(ShaderType.Fragment,new[]{"INSTANCING_ON"},
                ShaderCompilerPlatform.GLES3x,BuildTarget.Android,GraphicsTier.Tier2,true);
            Assert.IsTrue(result.Success,string.Join("\n",result.Messages.Select(message=>message.message)));
        }

        [TestCase(ShaderType.Vertex)] [TestCase(ShaderType.Fragment)]
        public void Lamp_glow_compiles_on_android_GLES3(ShaderType stage)
        {
            var shader=Resources.Load<Shader>("Shaders/HotelLampGlow");Assert.IsNotNull(shader);
            var result=ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,new string[0],
                ShaderCompilerPlatform.GLES3x,BuildTarget.Android,GraphicsTier.Tier2,true);
            Assert.IsTrue(result.Success,string.Join("\n",result.Messages.Select(message=>message.message)));
        }

        [TestCase(false, GraphicsDeviceType.OpenGLES3, "Adreno (TM) 640", "OpenGL ES 3.2", false)]
        [TestCase(true, GraphicsDeviceType.OpenGLES3, "Android Emulator OpenGL ES Translator (Google SwiftShader)", "OpenGL ES 3.0 (OpenGL ES 3.0 SwiftShader 4.0.0.1)", false)]
        [TestCase(true, GraphicsDeviceType.OpenGLES3, "Android Emulator OpenGL ES Translator (Google SwiftShader)", "OpenGL ES 3.0 (OpenGL ES 3.0 SwiftShader 5.0.0.1)", true)]
        [TestCase(true, GraphicsDeviceType.OpenGLES3, "Adreno (TM) 640", "OpenGL ES 3.2 V@512.503.0", true)]
        [TestCase(true, GraphicsDeviceType.OpenGLES3, "Mali-G78", "OpenGL ES 3.2", true)]
        [TestCase(true, GraphicsDeviceType.Vulkan, "Android Emulator OpenGL ES Translator (Google SwiftShader)", "SwiftShader 4.0.0.1", true)]
        [TestCase(true, GraphicsDeviceType.OpenGLCore, "llvmpipe (LLVM 20.1.2)", "4.5 (Core Profile) Mesa 25.2.8", true)]
        [TestCase(true, GraphicsDeviceType.OpenGLES3, null, null, true)]
        public void Instancing_compatibility_fallback_is_limited_to_unsupported_or_observed_legacy_driver(
            bool reportedSupport, GraphicsDeviceType device, string name, string version, bool expected)
        {
            Assert.AreEqual(expected, WallInstances.CanUseInstancing(reportedSupport, device, name, version));
        }

        [Test] public void Player_build_hook_keeps_instancing_variants_for_runtime_only_wall_materials()
        {
            // The production hook is in Unity's predefined editor assembly. Discover it without
            // coupling this test assembly to that predefined assembly or changing project assembly layout.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("BadAppleHotel.EditorTools.WallShaderBuildGuard"))
                .FirstOrDefault(found => found != null);
            Assert.IsNotNull(type, "The player-build safeguard is missing.");
            Assert.IsTrue(typeof(IPreprocessBuildWithReport).IsAssignableFrom(type));
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            Assert.IsNotEmpty(settings);
            using (var serialized = new SerializedObject(settings[0]))
            {
                var stripping = serialized.FindProperty("m_InstancingStripping");
                Assert.IsNotNull(stripping);
                int previous = stripping.intValue;
                try
                {
                    // Reproduce a fresh project's scene-only stripping policy. No wall material
                    // asset exists for Unity's scanner because the renderer creates it at runtime.
                    stripping.intValue = 0;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    var hook = (IPreprocessBuildWithReport)Activator.CreateInstance(type);
                    hook.OnPreprocessBuild(null);
                    serialized.Update();
                    Assert.AreEqual(2, stripping.intValue, "Player builds would strip the instanced hotel-wall shader.");
                    hook.OnPreprocessBuild(null);
                    serialized.Update();
                    Assert.AreEqual(2, stripping.intValue, "The safeguard must remain stable when called twice.");
                }
                finally
                {
                    stripping.intValue = previous;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings[0]);
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }
}
