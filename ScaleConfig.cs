﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TweakScale
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class TechUpdater : MonoBehaviour
    {
        public void Start()
        {
            Tech.Reload();
        }
    }

    public static class Tech
    {
        private static HashSet<string> _unlockedTechs = new HashSet<string>();

        public static void Reload()
        {
            if (HighLogic.CurrentGame == null)
                return;
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER && HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX)
                return;

            var persistentfile = KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder + "/persistent.sfs";
            var config = ConfigNode.Load(persistentfile);
            var gameconf = config.GetNode("GAME");
            var scenarios = gameconf.GetNodes("SCENARIO");
            var thisScenario = scenarios.FirstOrDefault(a => a.GetValue("name") == "ResearchAndDevelopment");
            if (thisScenario == null)
                return;
            var techs = thisScenario.GetNodes("Tech");

            _unlockedTechs = techs.Select(a => a.GetValue("id")).ToHashSet();
            _unlockedTechs.Add("");
        }

        public static bool IsUnlocked(string techId)
        {
            if (HighLogic.CurrentGame == null)
                return true;
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER && HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX)
                return true;
            return techId == "" || _unlockedTechs.Contains(techId);
        }
    }

    /// <summary>
    /// Configuration values for TweakScale.
    /// </summary>
    public class ScaleConfig
    {
        /// <summary>
        /// Fetches the scale config with the specified name.
        /// </summary>
        /// <param name="name">The name of the config to fetch.</param>
        /// <returns>The specified config or the default config if none exists by that name.</returns>
        private static ScaleConfig GetScaleConfig(string name)
        {
            var config = GameDatabase.Instance.GetConfigs("SCALETYPE").FirstOrDefault(a => a.name == name);
            if (config == null && name != "default")
            {
                Tools.LogWf("No SCALETYPE with name {0}", name);
            }
            return (object)config == null ? DefaultConfig : new ScaleConfig(config.config);
        }

        private static ScaleConfig[] _configs;
        public static ScaleConfig[] AllConfigs
        {
            get {
                return _configs = _configs ??
                        (GameDatabase.Instance.GetConfigs("SCALETYPE")
                            .Select(a => new ScaleConfig(a.config))
                            .ToArray());
            }
        }

        private static readonly ScaleConfig DefaultConfig = new ScaleConfig();

        private readonly float[] _scaleFactors = { 0.625f, 1.25f, 2.5f, 3.75f, 5f };
        private readonly string[] _scaleNames = { "62.5cm", "1.25m", "2.5m", "3.75m", "5m" };
        public readonly Dictionary<string, ScaleExponents> Exponents = new Dictionary<string, ScaleExponents>();

        public readonly bool IsFreeScale = false;
        public readonly string[] TechRequired = { "", "", "", "", "" };
        public readonly float MinValue = 0.625f;
        public readonly float MaxValue = 5.0f;
        public readonly float DefaultScale = 1.25f;
        public readonly string Suffix = "m";
        public readonly string Name;

        public float[] AllScaleFactors
        {
            get
            {
                return _scaleFactors;
            }
        }

        public float[] ScaleFactors
        {
            get
            {
                var result = _scaleFactors.ZipFilter(TechRequired, Tech.IsUnlocked).ToArray();
                return result;
            }
        }

        public string[] ScaleNames
        {
            get
            {
                var result = _scaleNames.ZipFilter(TechRequired, Tech.IsUnlocked).ToArray();
                return result;
            }
        }

        public int[] ScaleNodes { get; private set; }

        private ScaleConfig()
        {
            ScaleNodes = new int[] {};
        }

        public ScaleConfig(ConfigNode config)
        {
            ScaleNodes = new int[] {};
            if ((object)config == null || Tools.ConfigValue(config, "name", "default") == "default")
            {
                return; // Default values.
            }

            var type = Tools.ConfigValue(config, "type", "default");
            var source = GetScaleConfig(type);

            IsFreeScale   = Tools.ConfigValue(config, "freeScale",    source.IsFreeScale);
            MinValue      = Tools.ConfigValue(config, "minScale",     source.MinValue);
            MaxValue      = Tools.ConfigValue(config, "maxScale",     source.MaxValue);
            Suffix        = Tools.ConfigValue(config, "suffix",       source.Suffix);
            _scaleFactors = Tools.ConfigValue(config, "scaleFactors", source._scaleFactors);
            ScaleNodes    = Tools.ConfigValue(config, "scaleNodes",   source.ScaleNodes);
            _scaleNames   = Tools.ConfigValue(config, "scaleNames",   source._scaleNames).Select(a => a.Trim()).ToArray();
            TechRequired  = Tools.ConfigValue(config, "techRequired", source.TechRequired).Select(a=>a.Trim()).ToArray();
            Name          = Tools.ConfigValue(config, "name",         "unnamed scaletype");
            if (Name == "TweakScale")
            {
                Name = source.Name;
            }

            if (_scaleFactors.Length != _scaleNames.Length)
            {
                Tools.LogWf("Wrong number of scaleFactors compared to scaleNames: {0} scaleFactors vs {1} scaleNames", _scaleFactors.Length, _scaleNames.Length);
            }

            if (TechRequired.Length < _scaleFactors.Length)
            {
                TechRequired = TechRequired.Concat("".Repeat()).Take(_scaleFactors.Length).ToArray();
            }

            var tmpScale = Tools.ConfigValue(config, "defaultScale", source.DefaultScale);
            if (!IsFreeScale)
            {
                tmpScale = Tools.Closest(tmpScale, AllScaleFactors);
            }
            DefaultScale = Tools.Clamp(tmpScale, MinValue, MaxValue);

            Exponents = ScaleExponents.CreateExponentsForModule(config, source.Exponents);
        }

        public override string ToString()
        {
            var result = "ScaleConfig {\n";
            result += "	isFreeScale = " + IsFreeScale + "\n";
            result += "	scaleFactors = " + ScaleFactors + "\n";
            result += " scaleNodes = " + ScaleNodes + "\n";
            result += "	minValue = " + MinValue + "\n";
            result += "	maxValue = " + MaxValue + "\n";
            return result + "}";
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((ScaleConfig) obj);
        }

        public static bool operator ==(ScaleConfig a, ScaleConfig b)
        {
            if ((object)a == null)
                return (object)b == null;
            if ((object)b == null)
                return false;
            return a.Name == b.Name;
        }

        public static bool operator !=(ScaleConfig a, ScaleConfig b)
        {
            return !(a == b);
        }

        protected bool Equals(ScaleConfig other)
        {
            return string.Equals(Name, other.Name);
        }

        public override int GetHashCode()
        {
            return (Name != null ? Name.GetHashCode() : 0);
        }
    }
}
