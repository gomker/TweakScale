using KSPAPIExtensions;
using System;
using System.Linq;
using UnityEngine;

namespace TweakScale
{
    /// <summary>
    /// Converts from Gaius' GoodspeedTweakScale to updated TweakScale.
    /// </summary>
    public class GoodspeedTweakScale : TweakScale
    {
        private bool _updated;

        protected override void Setup()
        {
            base.Setup();
            if (_updated)
                return;
            tweakName = (int)tweakScale;
            tweakScale = ScaleFactors[tweakName];
            _updated = true;
        }
    }

    public class TweakScale : PartModule, IPartCostModifier
    {
        /// <summary>
        /// The selected scale. Different from currentScale only for destination single update, where currentScale is set to match this.
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Scale", guiFormat = "S4", guiUnits = "m")]
        [UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.625f, maxValue = 5, incrementLarge = 1.25f, incrementSmall = 0.125f, incrementSlide = 0.001f)]
        public float tweakScale = 1;

        /// <summary>
        /// Index into scale values array.
        /// </summary>
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Scale")]
        [UI_ChooseOption(scene = UI_Scene.Editor)]
        public int tweakName = 0;

        /// <summary>
        /// The scale to which the part currently is scaled.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float currentScale = -1;

        /// <summary>
        /// The default scale, i.e. the number by which to divide tweakScale and currentScale to get the relative size difference from when the part is used without TweakScale.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float defaultScale = -1;

        /// <summary>
        /// Whether the part should be freely scalable or limited to destination list of allowed values.
        /// </summary>
        [KSPField(isPersistant = true)]
        public bool isFreeScale = false;

        /// <summary>
        /// The version of TweakScale last used to change this part. Intended for use in the case of non-backward-compatible changes.
        /// </summary>
        [KSPField(isPersistant = true)]
        public string version;

        /// <summary>
        /// The scale exponentValue array. If isFreeScale is false, the part may only be one of these scales.
        /// </summary>
        protected float[] ScaleFactors = { 0.625f, 1.25f, 2.5f, 3.75f, 5f };
        
        /// <summary>
        /// The node scale array. If node scales are defined the nodes will be resized to these values.
        ///</summary>
        protected int[] ScaleNodes = { };

        /// <summary>
        /// The unmodified prefab part. From this, default values are found.
        /// </summary>
        private Part _prefabPart;

        /// <summary>
        /// Like currentScale above, this is the current scale vector. If TweakScale supports non-uniform scaling in the future (e.g. changing only the length of destination booster), savedScale may represent such destination scaling, while currentScale won't.
        /// </summary>
        private Vector3 _savedScale;

        /// <summary>
        /// The exponentValue by which the part is scaled by default. When destination part uses MODEL { scale = ... }, this will be different from (1,1,1).
        /// </summary>
        [KSPField(isPersistant = true)]
        public Vector3 defaultTransformScale = new Vector3(0f, 0f, 0f);


        //[KSPField(isPersistant = true)]
        private bool _firstUpdateWithParent = true;
        private bool _setupRun;
        private bool _invalidCfg;

        /// <summary>
        /// Updaters for different PartModules.
        /// </summary>
        private IRescalable[] _updaters;

        private enum Tristate
        {
            True,
            False,
            Unset
        }

        /// <summary>
        /// Whether this instance of TweakScale is the first. If not, log an error and make sure the TweakScale modules don't harmfully interact.
        /// </summary>
        private Tristate _duplicate = Tristate.Unset;

        /// <summary>
        /// The Config for this part.
        /// </summary>
        public ScaleConfig Config { get; private set; }

        /// <summary>
        /// Cost of unscaled, empty part.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float DryCost;

        /// <summary>
        /// The ConfigNode that belongs to the part this modules affects.
        /// </summary>
        private ConfigNode PartNode
        {
            get
            {
                return GameDatabase.Instance.GetConfigs("PART").Single(c => c.name.Replace('_', '.') == part.partInfo.name)
                    .config;
            }
        }

        /// <summary>
        /// The ConfigNode that belongs to this modules.
        /// </summary>
        public ConfigNode ModuleNode
        {
            get
            {
                return PartNode.GetNodes("MODULE").FirstOrDefault(n => n.GetValue("name") == moduleName);
            }
        }

        /// <summary>
        /// The current scaling factor.
        /// </summary>
        public ScalingFactor ScalingFactor
        {
            get
            {
                return new ScalingFactor(tweakScale / defaultScale, tweakScale / currentScale, isFreeScale ? -1 : tweakName);
            }
        }

        /// <summary>
        /// The smallest scale the part can be.
        /// </summary>
        private float MinSize
        {
            get
            {
                if (!isFreeScale)
                    return ScaleFactors.Min();
                var range = (UI_FloatEdit)Fields["tweakScale"].uiControlEditor;
                return range.minValue;
            }
        }

        /// <summary>
        /// The largest scale the part can be.
        /// </summary>
        internal float MaxSize
        {
            get
            {
                if (!isFreeScale)
                    return ScaleFactors.Max();
                var range = (UI_FloatEdit)Fields["tweakScale"].uiControlEditor;
                return range.maxValue;
            }
        }

        /// <summary>
        /// Loads settings from <paramref name="config"/>.
        /// </summary>
        /// <param name="config">The settings to use.</param>
        private void SetupFromConfig(ScaleConfig config)
        {
            isFreeScale = config.IsFreeScale;
            defaultScale = config.DefaultScale;
            Fields["tweakScale"].guiActiveEditor = false;
            Fields["tweakName"].guiActiveEditor = false;
            if (isFreeScale)
            {
                Fields["tweakScale"].guiActiveEditor = true;
                var range = (UI_FloatEdit)Fields["tweakScale"].uiControlEditor;
                range.minValue = config.MinValue;
                range.maxValue = config.MaxValue;
                range.incrementLarge = (float)Math.Round((range.maxValue - range.minValue) / 10, 2);
                range.incrementSmall = (float)Math.Round(range.incrementLarge / 10, 2);
                Fields["tweakScale"].guiUnits = config.Suffix;
            }
            else
            {
                Fields["tweakName"].guiActiveEditor = config.ScaleFactors.Length > 1;
                var options = (UI_ChooseOption)Fields["tweakName"].uiControlEditor;

                if (ScaleFactors.Length <= 0)
                    return;
                ScaleFactors = config.ScaleFactors;
                ScaleNodes = config.ScaleNodes;
                options.options = config.ScaleNames;
            }
        }

        /// <summary>
        /// Sets up values from config, creates updaters, and sets up initial values.
        /// </summary>
        protected virtual void Setup()
        {
            if (part.partInfo == null)
            {
                return;
            }

            if (_setupRun)
            {
                return;
            }

            _prefabPart = PartLoader.getPartInfoByName(part.partInfo.name).partPrefab;

            _updaters = TweakScaleUpdater.CreateUpdaters(part).ToArray();

            SetupFromConfig(Config = new ScaleConfig(ModuleNode));


            var doUpdate = currentScale < 0f;
            if (doUpdate)
            {
                tweakScale = currentScale = defaultScale;
                DryCost = (float)(part.partInfo.cost - _prefabPart.Resources.Cast<PartResource>().Aggregate(0.0, (a, b) => a + b.maxAmount * b.info.unitCost));
                if (DryCost < 0)
                {
                    DryCost = 0;
                }
            }

            if (!isFreeScale && ScaleFactors.Length != 0)
            {
                tweakName = Tools.ClosestIndex(tweakScale, ScaleFactors);
                tweakScale = ScaleFactors[tweakName];
            }

            if (!doUpdate)
            {
                UpdateByWidth(false);
                foreach (var updater in _updaters)
                {
                    updater.OnRescale(ScalingFactor);
                }
            }
            _setupRun = true;
        }


        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if ((object)part.parent != null)
            {
                _firstUpdateWithParent = false;
            }
            Setup();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Setup();
        }

        public override void OnSave(ConfigNode node)
        {
            version = GetType().Assembly.GetName().Version.ToString();
            base.OnSave(node);
        }

        /// <summary>
        /// Moves <paramref name="node"/> to reflect the new scale. If <paramref name="movePart"/> is true, also moves attached parts.
        /// </summary>
        /// <param name="node">The node to move.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        /// <param name="movePart">Whether or not to move attached parts.</param>
        private void MoveNode(AttachNode node, AttachNode baseNode, bool movePart)
        {
            var oldPosition = node.position;
            node.position = baseNode.position * ScalingFactor.absolute.linear;
            if (movePart && node.attachedPart != null)
            {
                if (node.attachedPart == part.parent)
                    part.transform.Translate(oldPosition - node.position);
                else
                    node.attachedPart.transform.Translate(node.position - oldPosition, part.transform);
            }
            RescaleNode(node, baseNode);
        }

        /// <summary>
        /// Change the size of <paramref name="node"/> to reflect the new size of the part it's attached to.
        /// </summary>
        /// <param name="node">The node to resize.</param>
        /// <param name="baseNode">The same node, as found on the prefab part.</param>
        private void RescaleNode(AttachNode node, AttachNode baseNode)
        {
            if (isFreeScale)
            {
                node.size = (int)(baseNode.size + (tweakScale - defaultScale) / (MaxSize - MinSize) * 5);
            }
            else
            {
            	if (ScaleNodes.Length > 0)
            	{
            		node.size = baseNode.size + (1 * ScaleNodes[tweakName]);
            	}
            	else
            	{
                    node.size = (int)(baseNode.size + (Tools.ClosestIndex(tweakScale, Config.AllScaleFactors) - Tools.ClosestIndex(defaultScale, Config.AllScaleFactors)) / (float)Config.AllScaleFactors.Length * 5);
                }
            }
            if (node.size < 0)
            {
                node.size = 0;
            }
        }

        /// <summary>
        /// Updates properties that change linearly with scale.
        /// </summary>
        /// <param name="moveParts">Whether or not to move attached parts.</param>
        private void UpdateByWidth(bool moveParts)
        {
            if (defaultTransformScale.x == 0.0f)
            {
                defaultTransformScale = part.transform.GetChild(0).localScale;
            }

            _savedScale = part.transform.GetChild(0).localScale = ScalingFactor.absolute.linear * defaultTransformScale;
            part.transform.GetChild(0).hasChanged = true;
            part.transform.hasChanged = true;

            foreach (var node in part.attachNodes)
            {
                var nodesWithSameId = part.attachNodes
                    .Where(a => a.id == node.id)
                    .ToArray();
                var idIdx = Array.FindIndex(nodesWithSameId, a => a == node);
                var baseNodesWithSameId = _prefabPart.attachNodes
                    .Where(a => a.id == node.id)
                    .ToArray();
                if (idIdx < baseNodesWithSameId.Length)
                {
                    var baseNode = baseNodesWithSameId[idIdx];

                    MoveNode(node, baseNode, moveParts);
                }
                else
                {
                    Tools.LogWf("Error scaling part. Node {0} does not have counterpart in base part.", node.id);
                }
            }

            if (part.srfAttachNode != null)
            {
                MoveNode(part.srfAttachNode, _prefabPart.srfAttachNode, moveParts);
            }
            if (moveParts)
            {
                foreach (var child in part.children)
                {
                    if (child.srfAttachNode == null || child.srfAttachNode.attachedPart != part)
                        continue;
                    var attachedPosition = child.transform.localPosition + child.transform.localRotation * child.srfAttachNode.position;
                    var targetPosition = attachedPosition * ScalingFactor.relative.linear;
                    child.transform.Translate(targetPosition - attachedPosition, part.transform);
                }
            }
        }

        /// <summary>
        /// Whether the part holds any resources (fuel, electricity, etc).
        /// </summary>
        private bool HasResources
        {
            get
            {
                return part.Resources.Count > 0;
            }
        }

        /// <summary>
        /// Marks the right-click window as dirty (i.e. tells it to update).
        /// </summary>
        private void UpdateWindow() // redraw the right-click window with the updated stats
        {
            if (isFreeScale || !HasResources)
                return;
            foreach (UIPartActionWindow win in FindObjectsOfType(typeof(UIPartActionWindow)))
            {
                if (win.part == part)
                {
                    // This causes the slider to be non-responsive - i.e. after you click once, you must click again, not drag the slider.
                    win.displayDirty = true;
                }
            }
        }

        void OnTweakScaleChanged()
        {
            if (!isFreeScale)
            {
                tweakScale = ScaleFactors[tweakName];
            }

            if (!Input.GetKey(KeyCode.LeftShift))
            {
                foreach (var child in part.children)
                {
                    var ts = child.Modules.OfType<TweakScale>().FirstOrDefault();
                    if ((object)ts == null)
                        continue;
                    if (ts.Config != Config)
                        continue;
                    if (ts.tweakScale != currentScale)
                        continue;
                    ts.tweakScale = tweakScale;
                    if (!isFreeScale)
                    {
                        ts.tweakName = tweakName;
                    }
                    ts.OnTweakScaleChanged();
                }
            }

            UpdateByWidth(true);
            UpdateWindow();

            foreach (var updater in _updaters)
            {
                updater.OnRescale(ScalingFactor);
            }
            currentScale = tweakScale;
        }

        private bool CheckForDuplicateTweakScale()
        {
            if (_duplicate == Tristate.False)
                return false;
            if (_duplicate == Tristate.True)
            {
                return true;
            }
            if (this != part.Modules.OfType<TweakScale>().First())
            {
                Tools.LogWf("Duplicate TweakScale module on part [{0}] {1}", part.partInfo.name, part.partInfo.title);
                Fields["tweakScale"].guiActiveEditor = false;
                Fields["tweakName"].guiActiveEditor = false;
                _duplicate = Tristate.True;
                return true;
            }
            _duplicate = Tristate.False;
            return false;
        }

        bool CheckForInvalidCfg()
        {
            if (ScaleFactors.Length != 0) 
                return false;
            if (_invalidCfg) 
                return true;

            _invalidCfg = true;
            Tools.LogWf("{0}({1}) has no valid scale factors. This is probably caused by an invalid TweakScale configuration for the part.", part.name, part.partInfo.title);
            return true;
        }

        public void Update()
        {
            if (CheckForDuplicateTweakScale() || CheckForInvalidCfg())
            {
                return;
            }

            if (_firstUpdateWithParent && (object)part.parent != null)
            {
                var ts = part.parent.Modules.OfType<TweakScale>().FirstOrDefault();
                if ((object)ts != null && ts.Config == Config)
                {
                    Tools.Logf("Changing size based on parent! ts: {0} this: {1}", ts.Config.Name, Config.Name);
                    tweakName = ts.tweakName;
                    tweakScale = ts.tweakScale;
                }
                _firstUpdateWithParent = false;
            }

            if (HighLogic.LoadedSceneIsEditor && currentScale >= 0f)
            {
                var changed = currentScale != (isFreeScale ? tweakScale : ScaleFactors[tweakName]);

                if (changed) // user has changed the scale tweakable
                {
                    OnTweakScaleChanged();
                }
                else if (part.transform.GetChild(0).localScale != _savedScale) // editor frequently nukes our OnStart resize some time later
                {
                    UpdateByWidth(false);
                }
            }

            foreach (var upd in _updaters.OfType<IUpdateable>())
            {
                upd.OnUpdate();
            }
        }

        public float GetModuleCost()
        {
            if (!_setupRun)
            {
                Setup();
            }
            return (float)(DryCost - part.partInfo.cost + part.Resources.Cast<PartResource>().Aggregate(0.0, (a, b) => a + b.maxAmount * b.info.unitCost));
        }
    }
}