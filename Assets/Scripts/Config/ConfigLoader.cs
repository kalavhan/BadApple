using System;
using System.IO;
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
            foreach (var t in c.towers.towers)
            {
                if (t.damageType == "none") continue;
                Require(t.rangeClass == "short" || t.rangeClass == "mid" || t.rangeClass == "long",
                    $"towers.json: weapon '{t.id}' needs rangeClass short, mid or long");
            }
            Require(c.map != null && c.map.width >= 32 && c.map.height >= 24, "map.json: map must be at least 32 x 24");
            Require(c.map.letters != null && c.map.letters.Length > 0, "map.json: no letters");
            Require(c.map.lotWidthMin >= 7 && c.map.lotHeightMin >= 7, "map.json: lots must be at least 7 x 7");
            Require(c.residents != null && c.residents.moveSpeed > 0, "residents.json: moveSpeed must be positive");
            Require(c.monsters.monsters != null && c.monsters.monsters.Length > 0, "monsters.json: no monsters");
            Require(c.abilities.starterLoadout != null && c.abilities.starterLoadout.Length == c.abilities.loadoutSize,
                "abilities.json: starterLoadout length must equal loadoutSize");

            foreach (var id in c.abilities.starterLoadout)
                Require(Array.Exists(c.abilities.abilities, a => a.id == id),
                    $"abilities.json: starterLoadout references unknown ability '{id}'");

            Require(c.monsters.resistanceTracks.damageTakenMultiplierByLevel.Length == c.monsters.resistanceTracks.maxLevel + 1,
                "monsters.json: damageTakenMultiplierByLevel needs maxLevel + 1 entries");
            Require(c.monsters.resistanceTracks.upgradeCostByLevel.Length == c.monsters.resistanceTracks.maxLevel + 1,
                "monsters.json: upgradeCostByLevel needs maxLevel + 1 entries");
        }

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException(message);
        }
    }
}
