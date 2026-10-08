using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine;

namespace BadAppleHotel.Config
{
    /// <summary>
    /// Loads and validates every JSON file in StreamingAssets/Config.
    /// Call ConfigLoader.Load() once at startup; throws with a clear message if a file is missing or invalid.
    /// On Android StreamingAssets is inside the APK, so use LoadFromJson with text read via UnityWebRequest there.
    /// </summary>
    public static class ConfigLoader
    {
        public const string Folder = "Config";

        public static GameConfig Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, Folder);
            return LoadFromJson(name => File.ReadAllText(Path.Combine(dir, name + ".json")));
        }

        /// <summary>Android packages StreamingAssets inside the APK; read jar URLs asynchronously.</summary>
        public static IEnumerator LoadForPlayer(Action<GameConfig> loaded, Action<Exception> failed)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                GameConfig config;
                try { config = Load(); }
                catch (Exception e) { failed(e); yield break; }
                loaded(config);
                yield break;
            }
            var texts = new Dictionary<string, string>();
            foreach (var name in new[] { "match", "economy", "beds", "doors", "towers", "monsters", "bodyparts", "abilities", "map", "residents", "monster_progression", "minions" })
            {
                string url = Application.streamingAssetsPath + "/" + Folder + "/" + name + ".json";
                using (var request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        failed(new InvalidDataException("Config '" + name + ".json' could not be read: " + request.error));
                        yield break;
                    }
                    texts[name] = request.downloadHandler.text;
                }
            }
            GameConfig result;
            try { result = LoadFromJson(name => texts[name]); }
            catch (Exception e) { failed(e); yield break; }
            loaded(result);
        }

        /// <summary>Loads from any text source, keyed by file name without extension (e.g. "match").</summary>
        public static GameConfig LoadFromJson(Func<string, string> readText)
        {
            var cfg = new GameConfig
            {
                match = Parse<MatchConfig>(readText, "match"),
                economy = Parse<EconomyConfig>(readText, "economy"),
                beds = Parse<BedsConfig>(readText, "beds"),
                doors = Parse<DoorsConfig>(readText, "doors"),
                towers = Parse<TowersConfig>(readText, "towers"),
                monsters = Parse<MonstersConfig>(readText, "monsters"),
                bodyParts = Parse<BodyPartsConfig>(readText, "bodyparts"),
                abilities = Parse<AbilitiesConfig>(readText, "abilities"),
                map = Parse<MapConfig>(readText, "map"),
                residents = Parse<ResidentsConfig>(readText, "residents"),
                progression = Parse<MonsterProgressionConfig>(readText, "monster_progression"),
                minions = Parse<MinionsConfig>(readText, "minions"),
            };
            Validate(cfg);
            return cfg;
        }

        static T Parse<T>(Func<string, string> readText, string name)
        {
            string json;
            try { json = readText(name); }
            catch (Exception e) { throw new InvalidDataException($"Config '{name}.json' could not be read: {e.Message}"); }

            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception e) { throw new InvalidDataException($"Config '{name}.json' is not valid: {e.Message}"); }
        }

        static void Validate(GameConfig c)
        {
            Require(c.match != null && c.match.playersPerMatch == c.match.monsterCount + c.match.residentCount,
                "match.json: playersPerMatch must equal monsterCount + residentCount");
            Require(c.match.roomCount >= c.match.residentCount,
                "match.json: roomCount must be at least residentCount");
            Require(c.beds.levels != null && c.beds.levels.Length > 0, "beds.json: no levels");
            Require(c.doors.levels != null && c.doors.levels.Length > 0, "doors.json: no levels");
            Require(c.towers.towers != null && c.towers.towers.Length > 0, "towers.json: no towers");
            Require(c.towers.rangeTiles != null && c.towers.rangeTiles.@short > 0 && c.towers.rangeTiles.mid > 0 && c.towers.rangeTiles.@long > 0,
                "towers.json: rangeTiles needs short, mid and long");
            var forms = c.towers.levels?.formStartLevels;
            if (forms != null && forms.Length > 0)
            {
                Require(forms[0] == 1, "towers.json: levels.formStartLevels must start at 1");
                for (int i = 1; i < forms.Length; i++)
                    Require(forms[i] > forms[i - 1] + 1, "towers.json: every form needs at least two levels");
                Require(c.towers.levels.maxLevel > forms[forms.Length - 1], "towers.json: levels.maxLevel must leave the final form a level-up");
                Require(c.towers.levels.inFormGrowth >= 0 && c.towers.levels.inFormGrowth <= 1 && c.towers.levels.levelUpShare > 0 && c.towers.levels.levelUpShare < 1 &&
                    c.towers.levels.levelUpCostGrowth >= 1 && c.towers.levels.finalFormCostMultiplier > 0, "towers.json: invalid level growth or costs");
            }
            foreach (var t in c.towers.towers)
            {
                if (t.tiers != null && t.tiers.Length > 0)
                {
                    var sprites = new System.Collections.Generic.HashSet<string>();
                    int support = 0;
                    for (int i = 0; i < t.tiers.Length; i++)
                    {
                        var tier = t.tiers[i];
                        Require(tier != null && !string.IsNullOrEmpty(tier.name) && !string.IsNullOrEmpty(tier.sprite),
                            $"towers.json: '{t.id}' tier {i + 1} needs a name and sprite");
                        Require(sprites.Add(tier.sprite), $"towers.json: '{t.id}' tiers need distinct art");
                        Require(tier.doorSupportLevel > support, $"towers.json: '{t.id}' door support must increase");
                        support = tier.doorSupportLevel;
                        Require(i == t.tiers.Length - 1 ? tier.upgradeCost == 0 : tier.upgradeCost > 0,
                            $"towers.json: '{t.id}' tier upgrade costs must be positive except at max tier");
                        Require(tier.damage >= 0 && tier.shotsPerSecond >= 0 && tier.range >= 0 && tier.faithPerSecond >= 0 && tier.dreamPerSecond >= 0,
                            $"towers.json: '{t.id}' has negative tier stats");
                    }
                }
                if (t.damageType == "none") continue;
                Require(t.rangeClass == "short" || t.rangeClass == "mid" || t.rangeClass == "long",
                    $"towers.json: weapon '{t.id}' needs rangeClass short, mid or long");
            }
            Require(c.map != null && c.map.width >= 32 && c.map.height >= 24, "map.json: map must be at least 32 x 24");
            Require(c.map.corridorWidth >= 1 && c.map.corridorWidth <= 5 && c.map.minDoorDistance > 0 &&
                c.map.maxNearestDoorDistance >= c.map.minDoorDistance, "map.json: invalid corridor width or door spacing");
            Require(c.bodyParts.minSpacingTiles > 0, "bodyparts.json: minSpacingTiles must be positive");
            Require(c.map.roomMinBuildTiles >= 20 && c.map.roomInteriorMin.x >= 6 && c.map.roomInteriorMin.y >= 5,
                "map.json: rooms need at least 20 usable build tiles and at least 6 x 5 interiors");
            Require(c.map.roomBuildTiles != null && c.map.roomBuildTiles.Length == c.match.roomCount &&
                Array.TrueForAll(c.map.roomBuildTiles, n => n >= c.map.roomMinBuildTiles && n <= 30),
                "map.json: supply one build-space budget (20–30) per room");
            Require(c.map.lotWidthMin >= 7 && c.map.lotHeightMin >= 7, "map.json: lots must be at least 7 x 7");
            Require(c.residents != null && c.residents.moveSpeed > 0, "residents.json: moveSpeed must be positive");
            Require(c.monsters.monsters != null && c.monsters.monsters.Length > 0, "monsters.json: no monsters");
            Require(c.abilities.starterLoadout != null && c.abilities.starterLoadout.Length == c.abilities.loadoutSize,
                "abilities.json: starterLoadout length must equal loadoutSize");

            foreach (var id in c.abilities.starterLoadout)
                Require(Array.Exists(c.abilities.abilities, a => a.id == id),
                    $"abilities.json: starterLoadout references unknown ability '{id}'");

            var p = c.progression;
            Require(p != null && p.fear != null && p.maxLevel > 1 && p.levelPriceBase > 0 && p.levelPriceStep >= 0,
                "monster_progression.json: invalid Fear or level prices");
            Require(p.statTracks != null && p.statTracks.Length > 0 && Array.TrueForAll(p.statTracks, s => s.maxRank > 0),
                "monster_progression.json: statTracks need at least one rank each");
            Require(p.rankDamage != null && p.rankDamage.Length == 3 && p.rankCooldown != null && p.rankCooldown.Length == 3,
                "monster_progression.json: rankDamage and rankCooldown need three ranks");
            Require(p.abilityRankLevels != null && p.utilityLevels != null && p.growthLevels != null && p.utilityAbilities != null,
                "monster_progression.json: missing level milestones");
            foreach (var id in p.utilityAbilities)
                Require(Array.Exists(c.abilities.abilities, a => a.id == id), $"monster_progression.json: unknown utility ability '{id}'");
            var mc = c.minions;
            Require(mc != null && mc.pulseSeconds != null && mc.pulseSeconds.Length > 0 && mc.baseHorde > 0 && mc.maxHorde >= mc.baseHorde &&
                mc.swapEveryNights > 0 && mc.resistByRank != null && mc.resistByRank.Length > 0 && mc.upgrades != null && mc.lines != null,
                "minions.json: invalid pulses, horde sizes, swap interval, resistances or upgrades");
            foreach (var u in mc.upgrades)
                Require(u.costs != null && u.costs.Length > 0 && Array.TrueForAll(u.costs, v => v > 0), $"minions.json: upgrade '{u.id}' needs positive costs");
            foreach (var m in c.monsters.monsters)
            {
                Require(m.attackDamage > 0 && m.attackInterval > 0, $"monsters.json: '{m.id}' needs an attack");
                foreach (var id in new[] { m.area, m.special })
                    Require(Array.Exists(c.abilities.abilities, a => a.id == id), $"monsters.json: '{m.id}' uses unknown ability '{id}'");
                var line = Array.Find(mc.lines, l => l.id == m.minionLine);
                Require(line != null && line.forms != null && line.forms.Length > 0 && Array.TrueForAll(line.forms, f => f.health > 0 && f.speed > 0 && f.interval > 0),
                    $"minions.json: '{m.id}' needs a minion line '{m.minionLine}' with forms");
            }
            foreach (var m in c.monsters.monsters)
                Require(m.moveSpeed >= c.residents.moveSpeed * 1.1f, "monsters.json: every monster must outrun residents by at least 10%");
            Require(c.match.disguiseBuildEverySeconds.Length == 2 && c.match.disguiseBuildEverySeconds[0] > 0 &&
                c.match.disguiseBuildEverySeconds[1] >= c.match.disguiseBuildEverySeconds[0], "match.json: invalid disguise build interval");
        }

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException(message);
        }
    }
}
