//#define AUTO_CONSUME_ITEMS

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using static SurvivalProgression.Survival;
using IMyEntity = VRage.ModAPI.IMyEntity;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using VRage.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using Sandbox.Game.Localization;
using Sandbox.Game.World;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender;
using Sandbox.Game.Entities.Inventory;
using System.Linq;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Voxels.Planet;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.EntityComponents.GameLogic;
using VRage.Game.Entity.EntityComponents.Interfaces;
using IMyCharacter = VRage.Game.ModAPI.IMyCharacter;
using ITerminalAction = Sandbox.ModAPI.Interfaces.ITerminalAction;


namespace SurvivalProgression
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SurvivalKit), true, new[] {"SurvivalKit", "SurvivalKitLarge" })]
    public class MySurvivalKitLogic : MyGameLogicComponent
    {

        private MyInventory _inventory;

        public MyInventory Inventory
        {
            get
            {
                if (_inventory != null) return _inventory;
                _inventory = (MyInventory)ProductionBlock.GetInventory();
                return _inventory;
            }
        }
        public IMyProductionBlock ProductionBlock;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            ProductionBlock = (IMyProductionBlock)Entity;
            base.Init(objectBuilder);
            if (DoSimulation)
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        private bool _wasProducing;

        public override void UpdateAfterSimulation10()
        {
            base.UpdateAfterSimulation10();

            if(_wasProducing != ProductionBlock.IsProducing)
            {
                if (DoSimulation && _wasProducing)
                    CleanInventory(Inventory);
                _wasProducing = ProductionBlock.IsProducing;
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenGenerator), true, "WaterCondenser")]
    public class MyWaterCondenserLogic : MyGameLogicComponent
    {
        public IMyGasGenerator GasGenerator;
        public MyInventory Inventory;
        public MyResourceSourceComponent SourceComp;
        public MyOxygenGeneratorDefinition BlockDefinition;
        public MyResourceSinkComponent ResourceSink;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            GasGenerator = (IMyGasGenerator)Entity;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            SetupInventory();
            GasGenerator.AppendingCustomInfo += AppendCustomInfo;
            SourceComp = GasGenerator.Components.Get<MyResourceSourceComponent>();
            BlockDefinition = (MyOxygenGeneratorDefinition)MyDefinitionManager.Static.GetDefinition(GasGenerator.BlockDefinition);

            var sinkInfo = new MyResourceSinkInfo { ResourceTypeId = MyResourceDistributorComponent.ElectricityId, MaxRequiredInput = BlockDefinition.OperationalPowerConsumption, RequiredInputFunc = NewRequiredInputFunc };
            ResourceSink = new MyResourceSinkComponent();
            ResourceSink.Init(MyStringHash.GetOrCompute("Utility"), sinkInfo);
            ResourceSink.AddType(ref sinkInfo);
            Entity.Components.Add(ResourceSink);
            ResourceSink.Update();
        }

        private float NewRequiredInputFunc() => GasGenerator.Enabled ? BlockDefinition.OperationalPowerConsumption : BlockDefinition.StandbyPowerConsumption;

        public void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            var airDensity = GetAirDensity(GasGenerator);
            var humidity = GetHumidity(GasGenerator);
            info.Clear();

            info.Append("\n");
            if (!CanProduce)
            {
                info.Append("OFFLINE");
                info.Append("\n");
                AddHelpData(block, info);
                return;
            }

            info.Append("Air Density: ");
            info.Append((airDensity * 100f).ToString("F0"));
            info.Append("%\n");
            info.Append("Humidity: ");
            info.Append((humidity * 100f).ToString("F0"));
            info.Append("%\n");

            var position = GasGenerator.PositionComp.GetPosition();
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet != null && planet.HasAtmosphere && MySurvivalCharacter.InAtmosphere(planet, position))
            {
                var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
                var material = planet.GetMaterialAt(ref surfacePoint);
                if (material != null)
                {
                    info.Append("Biome: ");
                    info.Append(material.MaterialTypeNameId.String);
                    info.Append("\n");
                }
            }

            info.Append("Producing: ");
            info.Append(((_resourcePerUpdate / 1000f) * 60).ToString("F1"));
            info.Append("L\\min\n");

            AddHelpData(block, info);
        }

        void AddHelpData(IMyTerminalBlock block, StringBuilder info)
        {
            info.Append("\n");
            info.Append("Requires atmosphere and suitable biome to operate. Different biomes will provide different humidity values.");
        }

        void SetupInventory()
        {
            Inventory = (MyInventory)GasGenerator.GetInventory();
        }

        private float _resourcePerUpdate => _effectiveness * 1000;

        private float _effectiveness;
        private bool _hasMoisture => _effectiveness > 0f;

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            /*if (ResourceSink != null)
            {
                ResourceSink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, BlockDefinition.OperationalPowerConsumption);
                ResourceSink.Update();
            }*/
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            if (GasGenerator.Enabled && CanProduce)
            {
                _effectiveness = GetHumidity(GasGenerator) * GetAirDensity(GasGenerator);
            }
            else
            {
                _effectiveness = 0;
            }

            if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            {
                //UpdateState(1, true);
                GasGenerator.RefreshCustomInfo();
                UpdateTerminal(GasGenerator);
            }
        }

        private int _ticksSinceUpdate;

        private bool _isPowered;

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            ResourceSink.Update();

            if (DoSimulation)
            {
                if (CanProduce)
                {
                    if (GasGenerator.AutoRefill && CanRefill())
                        RefillBottles();

                    if (_ticksSinceUpdate < ONCE_PER_SECOND)
                    {
                        _ticksSinceUpdate += 100;
                    }
                    else
                    {
                        _isPowered = true;
                        _ticksSinceUpdate = 0;
                        SourceComp.SetRemainingCapacityByType(Hydration, _resourcePerUpdate);
                        ResourceSink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, BlockDefinition.OperationalPowerConsumption);
                        ResourceSink.Update();
                        SourceComp.Enabled = true;
                        //GasGenerator.ResourceSink.SetRequiredInputByType();
                    }
                } 
                else if (_isPowered)
                {
                    _isPowered = false;
                    _ticksSinceUpdate = 0;
                    ResourceSink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, BlockDefinition.StandbyPowerConsumption);
                    SourceComp.SetRemainingCapacityByType(Hydration, 0f);
                    ResourceSink.Update();
                    SourceComp.Enabled = false;
                }
            }
        }

        public bool CanProduce
        {
            get
            {
                return ResourceSink.IsPoweredByType(MyResourceDistributorComponent.ElectricityId)
                       && GasGenerator.Enabled
                       && GasGenerator.IsFunctional;
            }
        }

        private bool CanRefill()
        {
            if (!CanProduce || !_hasMoisture)
                return false;

            SetupInventory();

            var items = Inventory.GetItems();
            foreach (var item in items)
            {
                var oxygenContainer = item.Content as MyObjectBuilder_GasContainerObject;
                if (oxygenContainer == null)
                    continue;

                if (oxygenContainer.GasLevel < 1f)
                    return true;
            }

            return false;
        }


        private void RefillBottles()
        {
            if (Inventory == null)
                SetupInventory();

            var items = Inventory.GetItems();

            float gasProductionAmount = _resourcePerUpdate;

            float toProduce = 0f;

            foreach (var item in items)
            {
                if (gasProductionAmount <= 0f)
                    return;

                var gasContainer = item.Content as MyObjectBuilder_GasContainerObject;
                if (gasContainer == null || gasContainer.GasLevel >= 1f)
                    continue;

                var physicalItem = MyDefinitionManager.Static.GetPhysicalItemDefinition(gasContainer) as MyOxygenContainerDefinition;
                if (physicalItem == null || physicalItem.StoredGasId != Survival.Hydration)
                    continue;

                var bottleGasAmount = gasContainer.GasLevel * physicalItem.Capacity;

                var transferredAmount = Math.Min(physicalItem.Capacity - bottleGasAmount, gasProductionAmount);
                gasContainer.GasLevel = Math.Min((bottleGasAmount + transferredAmount) / physicalItem.Capacity, 1f);

                toProduce += transferredAmount;
                gasProductionAmount -= transferredAmount;
            }

            if (toProduce > 0f)
            {
                Inventory.RaiseContentsChanged();
            }
        }

    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CargoContainer), true, "OrganicsPlanter")]
    public class MyOrganicsPlanter : MyGameLogicComponent
    {
        public IMyTerminalBlock TerminalBlock;
        public IMyCargoContainer CargoBlock;
        public MyResourceSinkComponent ResourceSink;
        public MyInventory Inventory;
        private MyResourceSinkInfo WaterSinkInfo;

        private bool _producing;
        private bool _hasSunlight;
        private bool _hasWater;
        private float _habitability;
        private bool _hasDirt;
        private bool _hasPacketWater;

        public const float HYDRATION_PER_TICK = 0.1f;
        
        private float Sink_ComputeRequiredWater()
        {
            return _producing ? HYDRATION_PER_TICK : 0f;
        }

        private Vector3D CenterPoint => CargoBlock.GetPosition() +
                                        Vector3.Transform(Vector3.Forward * 1.1f, CargoBlock.WorldMatrix.GetOrientation());

        private static List<MyBlueprintDefinition> _blueprints;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (_blueprints == null)
            {
                _blueprints = new List<MyBlueprintDefinition>();
                var blueprintClass = MyDefinitionManager.Static.GetBlueprintClass("CommonAgriculture");
                foreach (var blueprint in blueprintClass)
                {
                    /*if (blueprint.InputItemType != typeof(MyObjectBuilder_Ore))
                        continue;*/
                    try
                    {
                        _blueprints.Add((MyBlueprintDefinition) blueprint);
                    }
                    catch (Exception e)
                    {
                    }
                }
            }

            TerminalBlock = (IMyTerminalBlock)Entity;
            CargoBlock = (IMyCargoContainer)Entity;
            WaterSinkInfo = new MyResourceSinkInfo { ResourceTypeId = Survival.Hydration, MaxRequiredInput = 100f, RequiredInputFunc = Sink_ComputeRequiredWater };
            ResourceSink = new MyResourceSinkComponent();
            ResourceSink.Init(MyStringHash.GetOrCompute("Utility"), WaterSinkInfo);
            ResourceSink.AddType(ref WaterSinkInfo);
            Entity.Components.Add(ResourceSink);
            ResourceSink.Update();

            base.Init(objectBuilder);

            SetupInventory();

            TerminalBlock.AppendingCustomInfo += AppendCustomInfo;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_FRAME;

            TerminalBlock.RefreshCustomInfo();
        }

        private static List<MyDefinitionId> _waterContainers;

        public static void CacheDefinitions()
        {
            if (_waterContainers != null)
                return;
            _waterContainers = new List<MyDefinitionId>();
            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
                if (MySurvivalCharacter.IsUsableItem(definition, Survival.Hydration))
                    _waterContainers.Add(definition.Id);
        }

        void SetupInventory()
        {
            Inventory = (MyInventory)TerminalBlock.GetInventory();
            if (Inventory == null) return;

            CacheDefinitions();

            Inventory.Constraint = new MyInventoryConstraint("dirt", "Textures/GUI/filter_organics.dds");

            foreach (var blueprint in _blueprints)
                Inventory.Constraint.Add(blueprint.Prerequisites[0].Id);

            foreach (var container in _waterContainers)
                Inventory.Constraint.Add(container);

            Inventory.SetFlags(MyInventoryFlags.CanReceive);
            Inventory.SetFlags(MyInventoryFlags.CanSend);
        }

        public void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            info.Clear();

            info.Append("MAY BE INACCURATE IN MULTIPLAYER");
            info.Append("\n\n");

            info.Append("Producing: ");
            info.Append(_producing);
            info.Append("\n");

            info.Append("Habitability: ");
            info.Append((_habitability * 100f).ToString("F0"));
            info.Append("%\n");

            if (_habitability <= 0)
                return;

            if (!_hasDirt)
            {
                info.Append("No Dirt!");
                info.Append("\n");
                return;
            }

            if (!_hasWater && !_hasPacketWater)
            {
                info.Append("No Water!");
                info.Append("\n");
                return;
            }

            if (!_hasSunlight)
            {
                info.Append("No Sunlight!");
                info.Append("\n");
            }
        }

        public override void UpdateBeforeSimulation()
        {
            base.UpdateBeforeSimulation();
            if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            {
                //UpdateState(1, true);
                TerminalBlock.RefreshCustomInfo();
                UpdateTerminal(TerminalBlock);
            }

            //MyAPIGateway.Utilities.ShowNotification($"oxygenRatio: {GetOxygenLevel(TerminalBlock):F2}", 1, MyFontEnum.Green);

            /*
            var sunDirection = MyVisualScriptLogicProvider.GetSunDirection();
            MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("Square"), !IsSunBlocked() ? Color.Green : Color.Red, CenterPoint, sunDirection, 100, 0.1f);
        */
        }

        private int _hasSunlightTicks;

        void UpdateState(int ticksPassed = 100, bool skipRaycast = false)
        {
            _hasWater = ResourceSink.ResourceAvailableByType(Survival.Hydration) > 0;
            var radiation = GetRadiationAt(CenterPoint);
            var oxygenRatio = GetOxygenLevel(TerminalBlock);
            if (oxygenRatio > 1)
                oxygenRatio = 1f;
            if (oxygenRatio < 0.2f)
                oxygenRatio = 0f;

            _habitability = (1 - radiation) * oxygenRatio;

            var habitable = _habitability > 0f;

            if (!skipRaycast)
            {
                if (!IsSunBlocked())
                {
                    if (!_hasSunlight)
                    {
                        _hasSunlightTicks += ticksPassed;
                        if (_hasSunlightTicks > ONCE_PER_SECOND * 3)
                            _hasSunlight = true;
                    }
                }
                else
                {
                    _hasSunlightTicks = 0;
                    _hasSunlight = false;
                }
            }
            _producing = habitable && (_hasWater || _hasPacketWater) && _hasSunlight;
        }

        void ProcessWater()
        {
            _hasPacketWater = false;
            var valueNeeded = _hasWater ? 0 : HYDRATION_PER_TICK;
            if (valueNeeded <= 0f) return;

            var j = 0;
            while (j < Inventory.ItemCount && j >= 0 && valueNeeded > 0f)
            {
                var itemCheck = Inventory.GetItemByIndex(j);
                if (itemCheck.HasValue)
                {
                    var item = itemCheck.Value;
                    if (item.Content is MyObjectBuilder_Ore)
                    {
                        j++;
                        continue;
                    }

                    if (DoSimulation && _hasDirt && _hasSunlight)
                    {
                        if (item.Content is MyObjectBuilder_OxygenContainerObject)
                        {
                            valueNeeded = MySurvivalCharacter.TakeAction(item, Survival.Hydration.SubtypeId, Inventory, valueNeeded);
                        }
                        else if (item.Content is MyObjectBuilder_ConsumableItem)
                        {
                            if (MySurvivalCharacter.IsUsableItem(item, Survival.Hydration.SubtypeId, true))
                            {
                                Inventory.RemoveItems(item.ItemId, (MyFixedPoint)valueNeeded);
                                _hasPacketWater = true;
                                break;
                            }
                        }
                        _hasPacketWater = valueNeeded <= 0f;
                    }
                    else
                    {
                        _hasPacketWater = MySurvivalCharacter.IsUsableItem(item, Survival.Hydration.SubtypeId, true);
                    }
                }
                j++;
            }
        }

        private int _ticksSinceProducing;

        static MyBlueprintDefinition GetBlueprint(MyDefinitionId definitionId)
        {
            foreach (var blueprint in _blueprints)
                if (definitionId == blueprint.Prerequisites[0].Id) return blueprint;
            return null;
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();

            if (Inventory == null)
                SetupInventory();

            ProcessWater();

            if (_blockedTimeout > 0)
                _blockedTimeout -= 100;

            _ticksSinceProducing += 100;
            if (_ticksSinceProducing <= ONCE_PER_SECOND * 2) return;

            _hasDirt = false;
            _ticksSinceProducing = 0;
            var j = 0;
            while (j < Inventory.ItemCount && j >= 0)
            {
                var itemCheck = Inventory.GetItemByIndex(j);
                if (itemCheck.HasValue)
                {
                    var item = itemCheck.Value;
                    var blueprint = GetBlueprint(item.Content.GetObjectId());
                    if (blueprint != null)
                    {
                        _hasDirt = true;
                        if (_producing && DoSimulation)
                        {
                            Inventory.RemoveItems(item.ItemId, blueprint.Prerequisites[0].Amount);
                            foreach (var result in blueprint.Results)
                            {
                                var resOb = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(result.Id.TypeId, result.Id.SubtypeName);
                                Inventory.AddItems(result.Amount * _habitability, resOb);
                            }
                        }
                        break;
                    }
                }
                j++;
            }
            UpdateState();

            if (_producing)
                ResourceSink.Update();

        }

        public static int NoVoxelCollisionLayer = MyAPIGateway.Physics.GetCollisionLayer("NoVoxelCollisionLayer");
        public static int VoxelCollisionLayer = MyAPIGateway.Physics.GetCollisionLayer("VoxelCollisionLayer");

        private int _blockedTimeout = 0;

        bool IsSunBlocked()
        {
            if (_blockedTimeout > 0)
                return true;

            var start = CenterPoint;
            var sunDirection = MyVisualScriptLogicProvider.GetSunDirection();

            var visible = Vector3.Dot(Vector3.Transform(Vector3.Forward, CargoBlock.WorldMatrix.GetOrientation()),
                sunDirection) >= 0;

            if (!visible)
                return true;

            var end = start + (sunDirection * 50f);
            var hitList = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(start, end, hitList, NoVoxelCollisionLayer);
            foreach (var hit in hitList)
            {
                if (IsBlocked(hit))
                {
                    _blockedTimeout = ONCE_PER_SECOND * 2;
                    return true;
                }
            }

            var rayToSun = new LineD(start, sunDirection * 20000f);
            var result = new List<MyLineSegmentOverlapResult<MyVoxelBase>>();
            MyGamePruningStructure.GetVoxelMapsOverlappingRay(ref rayToSun, result);
            if (result.Count > 0)
            {
                end = start + (sunDirection * 20000f);
                var voxelList = new List<IHitInfo>();
                MyAPIGateway.Physics.CastRay(start, end, voxelList, VoxelCollisionLayer);
                foreach (var hit in voxelList)
                    if (IsBlockedVoxel(hit))
                    {
                        _blockedTimeout = ONCE_PER_SECOND * 2;
                        return true;
                    }
            }
            return false;
        }

        public bool IsBlockedVoxel(IHitInfo hitInfo)
        {
            var voxel = hitInfo.HitEntity as IMyVoxelBase;
            if (voxel != null)
                return true;
            return false;
        }

        private static MyStringHash _glass = MyStringHash.GetOrCompute("Glass");
        private static MyStringHash _glassOpaque = MyStringHash.GetOrCompute("GlassOpaque");

        private static MyPhysicalMaterialDefinition _glassDef = MyDefinitionManager.Static.GetPhysicalMaterialDefinition("Glass");
        private static MyPhysicalMaterialDefinition _glassOpaqueDef = MyDefinitionManager.Static.GetPhysicalMaterialDefinition("GlassOpaque");

        public bool IsBlocked(IHitInfo hitInfo)
        {
            var cubeGrid = hitInfo.HitEntity as IMyCubeGrid;
            if (cubeGrid != null)
            {
                var pos = cubeGrid.WorldToGridInteger(hitInfo.Position);
                var block = cubeGrid.GetCubeBlock(pos);
                if (block != null)
                {
                    var definition = MyDefinitionManager.Static.GetDefinition(block.BlockDefinition.Id) as MyCubeBlockDefinition;
                    if (definition != null
                        && definition.PhysicalMaterial != _glassDef
                        && definition.PhysicalMaterial != _glassOpaqueDef)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Refinery), true, "OrganicsFarm")]
    public class MyOrganicsFarm : MyGameLogicComponent
    {
        public IMyTerminalBlock TerminalBlock;
        public IMyRefinery RefineryBlock;
        public IMyCubeGrid CubeGrid => RefineryBlock.CubeGrid;
        public MyResourceSinkComponent ResourceSink;

        private readonly MyDefinitionId _hydrationGasId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydration");

        private MyResourceSinkInfo WaterSinkInfo;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            TerminalBlock = (IMyTerminalBlock)Entity;
            RefineryBlock = (IMyRefinery)Entity;
            TerminalBlock.AppendingCustomInfo += AppendCustomInfo;
            ResourceSink = Entity.Components.Get<MyResourceSinkComponent>();
            _saveState = true;
            //RefineryBlock.OnUpgradeValuesChanged += RefineryBlock_OnUpgradeValuesChanged;
            //ResourceSink.IsPoweredChanged += Sink_IsPoweredChanged;
            ResourceSink.CurrentInputChanged += Sink_CurrentInputChanged;
            RefineryBlock.EnabledChanged += RefineryBlockOnEnabledChanged;

            WaterSinkInfo = new MyResourceSinkInfo { ResourceTypeId = _hydrationGasId, MaxRequiredInput = 100f, RequiredInputFunc = Sink_ComputeRequiredWater };
            ResourceSink.AddType(ref WaterSinkInfo);
            ResourceSink.SetMaxRequiredInputByType(_hydrationGasId, 5f);
            ResourceSink.SetRequiredInputFuncByType(_hydrationGasId, Sink_ComputeRequiredWater);
            _enabled = RefineryBlock.Enabled;
            _saveState = false;
            UpdateState(true);
            SetHabitability();
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_FRAME;
        }

        private void RefineryBlockOnEnabledChanged(IMyTerminalBlock obj)
        {
            if (_saveState)
            {
                _enabled = RefineryBlock.Enabled;
                SetHabitability();
                SetProductivity();
            }
            SetState();
        }

        private int _ticksSinceUpdate;
        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();
            _ticksSinceUpdate += 100;
            if (_ticksSinceUpdate > TICKS_PER_SECOND * 10f)
            {
                SetHabitability();
                SetProductivity();
                UpdateState();
                _ticksSinceUpdate = 0;
            }
        }

        bool IsAirtight(Vector3D position) => CubeGrid?.IsRoomAtPositionAirtight(CubeGrid.WorldToGridInteger(position)) ?? false;

        private float _hydration = 1f;
        private float _habitability = 1f;
        void SetHabitability()
        {
            var position = TerminalBlock.PositionComp.GetPosition();
            var radiation = GetRadiationAt(position);

            //MyAPIGateway.Utilities.ShowNotification($"radiation {radiation:F2} airtight: {IsAirtight(position)}", 1000, MyFontEnum.Green);

            if (IsAirtight(position))
                radiation *= 0.75f;

            _habitability = (1 - radiation);
        }

        private bool _hasHabitat;
        private bool _hasWater;
        private bool _saveState;
        private bool _enabled;

        public const float HYDRATION_PER_TICK = 0.1f;

        private float Sink_ComputeRequiredWater()
        {
            return RefineryBlock.Enabled ? HYDRATION_PER_TICK : 0f;
        }

        private void RefineryBlock_OnUpgradeValuesChanged() => UpdateState();
        private void Sink_IsPoweredChanged() => UpdateState();
        private void Sink_CurrentInputChanged(MyDefinitionId resourceTypeId, float oldInput, MyResourceSinkComponent sink) => UpdateState();

        void SetProductivity() => RefineryBlock.UpgradeValues["Effectiveness"] = (_habitability * _hydration);

        void UpdateState(bool force = false)
        {
            var newHasWater = ResourceSink.ResourceAvailableByType(_hydrationGasId) > 0;
            var newHasHabitat = _habitability > 0f;

            var newState = (newHasWater && newHasHabitat);
            if (newHasWater != _hasWater || force)
            {
                _hasWater = newHasWater;
                _hydration = !_hasWater ? 0f : 1f;
            }
            if (newState != _hasHabitat || force)
            {
                _hasHabitat = newHasHabitat;
            }
            SetProductivity();
            if (RefineryBlock.Enabled != (_enabled && newState))
            {
                SetState();
            }
            TerminalBlock.RefreshCustomInfo();
        }

        void SetState()
        {
            _saveState = false;
            RefineryBlock.Enabled = _hasWater && _hasHabitat && _enabled;
            _saveState = true;
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            UpdateState();
            ResourceSink.Update();
        }

        public float WaterAvailable => ResourceSink.ResourceAvailableByType(_hydrationGasId);

        public void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            info.Clear();
            info.Append("Hydration: ");
            info.Append((_hydration * 100f).ToString("F0"));
            info.Append("%\n");
            info.Append("Habitability: ");
            info.Append((_habitability * 100f).ToString("F0"));
            info.Append("%\n");
            info.Append("Has Water: ");
            info.Append(_hasWater.ToString());
            info.Append("\n");
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenGenerator), true, "LargeWaterGenerator")]
    public class MyWaterGenerator : MyGameLogicComponent
    {
        public IMyGasGenerator ProductionBlock;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            ProductionBlock = (IMyGasGenerator)Entity;
            ProductionBlock.AppendingCustomInfo += AppendCustomInfo;
            ProductionBlock.AddUpgradeValue("Productivity", 0f);
            ProductionBlock.AddUpgradeValue("Effectiveness", 1f);
            ProductionBlock.AddUpgradeValue("PowerEfficiency", 1f);
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            ProductionBlock.RefreshCustomInfo();
        }

        public void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            info.Clear();
            info.AppendFormat("\n");
            info.Append("Productivity: ");
            info.Append(((ProductionBlock.UpgradeValues["Productivity"] + 1f) * 100f).ToString("F0"));
            info.Append("%\n");
            info.Append("Effectiveness: ");
            info.Append(((ProductionBlock.UpgradeValues["Effectiveness"]) * 100f).ToString("F0"));
            info.Append("%\n");
            info.Append("Power Efficiency: ");
            info.Append(((ProductionBlock.UpgradeValues["PowerEfficiency"]) * 100f).ToString("F0"));
            info.Append("%\n");
        }
    }

    public class MySurvivalCharacter
    {
        public IMyCharacter Character;
        public MyInventoryBase Inventory;
        public MyCharacterStatComponent StatComponent;
        public MyEntityStat Hydration;
        public MyEntityStat Nutrition;
        public MyEntityStat Radiation;

        private float _radiationTicksPassed;
        private float _hydrationTicksPassed;
        private float _nutritionTicksPassed;
        private int _dehydrationDamageTicks;
        private int _starvationDamageTicks;
        private int _radiationDamageTicks;

        public float GetRadiationLevel()
        {
            if (Character == null) 
                return 0f;

            var position = Character.GetPosition();
            var radiationLevel = GetRadiationAt(position);
            if (Cockpit != null && Cockpit.BlockDefinition.IsPressurized)
            {
                radiationLevel *= 0.85f;
                return radiationLevel;
            }
            if (IsAirtight(Character.PositionComp.WorldAABB, position))
                radiationLevel *= 0.75f;
            if (Character.EnvironmentOxygenLevel > 0.8f)
                radiationLevel *= 0.8f;
            return radiationLevel;
        }

        public MySurvivalCharacter(IMyCharacter character, bool localOnly = false)
        {
            SurvivalSession.LogServer($"creating {character.DisplayName}");
            Character = character;
            Inventory = (MyInventoryBase)character.GetInventory();
            StatComponent = Character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
            if (StatComponent != null)
            {
                StatComponent.Stats.TryGetValue(RadiationId, out Radiation);
                StatComponent.Stats.TryGetValue(HydrationId, out Hydration);
                StatComponent.Stats.TryGetValue(NutritionId, out Nutrition);
            }
            if (!localOnly)
                SurvivalSession.Characters.Add(this);
        }

        public bool IsDead => Character == null || Character.IsDead;

        public bool IsHydrated
        {
            get
            {
                if (!IsValid) return true;
                return Hydration.Value > 0;
            }
        }
        public bool IsSatiated
        {
            get
            {
                if (!IsValid) return true;
                return Nutrition.Value > 0;
            }
        }
        public bool IsIrradiated
        {
            get
            {
                if (!IsValid) return true;
                return Radiation.Value >= 99;
            }
        }

        public bool IsDehydrated => !IsHydrated;
        public bool IsStarving => !IsSatiated;

        private IMyHudNotification _dehydrationNotice;
        private IMyHudNotification _starvingNotice;
        private IMyHudNotification _irradiationNotice;

        public static MyStringId CommonContext = MyStringId.GetOrCompute("Common");
        public static MyStringId StarvingText = MyStringId.GetOrCompute("Survival_Notification_Starving");
        public static MyStringId DehydratedText = MyStringId.GetOrCompute("Survival_Notification_Dehydrated");

        private static int _ticksSinceGUI = int.MaxValue;

        public static bool InAtmosphere(MyPlanet planet, Vector3D position)
        {
            return planet.HasAtmosphere && Vector3D.DistanceSquared(position, planet.WorldMatrix.Translation) <
                (planet.AtmosphereRadius * planet.AtmosphereRadius);
        }

        public static bool IsAirtight(BoundingBoxD worldAabb, Vector3D position)
        {
            // Get a list of entities near the character.
            var result = new List<MyEntity>();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref worldAabb, result);

            foreach (MyEntity myEntity in result)
            {
                IMyCubeGrid myCubeGrid = myEntity as IMyCubeGrid;
                if (myCubeGrid != null)
                {
                    // Easy check once we know which block to query the grid for.
                    if (myCubeGrid.IsRoomAtPositionAirtight(myCubeGrid.WorldToGridInteger(position)))
                        return true;
                }
            }
            return false;
        }

        public void Draw()
        {
            if (Character.IsDead) return;

            EnvironmentRadiationHudStat.SetValue(GetRadiationLevel() * 100f);

            /*var isSealed = IsAirtight();
            var value = Character.Components.Get<MyCharacterOxygenComponent>();
            MyAPIGateway.Utilities.ShowNotification($"oxygen {value.EnvironmentOxygenLevel} sealed {isSealed}", 10, MyFontEnum.Green);
            */

            // prepare stats for GUI occasionally
            if (_ticksSinceGUI > ONCE_PER_SECOND * 0.5f)
            {
                var items = Inventory?.GetItems();
                var waterBottles = 0;
                var foodItems = 0;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (IsUsableItem(item, Hydration.StatId)) waterBottles++;
                        if (IsUsableItem(item, Nutrition.StatId)) foodItems++;
                        if (waterBottles >= 3 && foodItems >= 3) break;
                    }
                }
                WaterBottles = waterBottles;
                FoodItems = foodItems;
                _ticksSinceGUI = 0;
            }
            else
            {
                _ticksSinceGUI++;
            }

            if (IsDehydrated)
            {
                if (_dehydrationNotice == null)
                {
                    //_dehydrationNotice = MyAPIGateway.Utilities.CreateNotification(MyTexts.Get(DehydratedText).ToString(), Survival.SURVIVIAL_NOTIFICATION_TIME, MyFontEnum.Red);
                    _dehydrationNotice = MyAPIGateway.Utilities.CreateNotification("You are dehydrated!", ONCE_PER_SECOND, MyFontEnum.Red);
                    _dehydrationNotice.Show();
                }
                else
                {
                    _dehydrationNotice.ResetAliveTime();
                }
            }
            if (IsStarving)
            {
                if (_starvingNotice == null)
                {
                    //_starvingNotice = MyAPIGateway.Utilities.CreateNotification(MyTexts.Get(StarvingText).ToString(), Survival.SURVIVIAL_NOTIFICATION_TIME, MyFontEnum.Red);
                    _starvingNotice = MyAPIGateway.Utilities.CreateNotification("You are starving!", ONCE_PER_SECOND, MyFontEnum.Red);
                    _starvingNotice.Show();
                }
                else
                {
                    _starvingNotice.ResetAliveTime();
                }
            }

            if (IsIrradiated)
            {
                if (_irradiationNotice == null)
                {
                    //_starvingNotice = MyAPIGateway.Utilities.CreateNotification(MyTexts.Get(StarvingText).ToString(), Survival.SURVIVIAL_NOTIFICATION_TIME, MyFontEnum.Red);
                    _irradiationNotice = MyAPIGateway.Utilities.CreateNotification("You have radiation sickness!", ONCE_PER_SECOND, MyFontEnum.Red);
                    _irradiationNotice.Show();
                }
                else
                {
                    _irradiationNotice.ResetAliveTime();
                }
            }
        }

        private static bool _inventoryUpdateNeeded;
        public bool IsDirty;


        public static bool IsUsableItem(MyPhysicalInventoryItem item, MyStringHash hash, bool includeConsumables = false)
        {
            var gasItem = item.Content as MyObjectBuilder_OxygenContainerObject;
            if (gasItem != null)
            {
                var definition = Find<MyOxygenContainerDefinition>(gasItem.SubtypeId);
                if (definition != null)
                    if (definition.StoredGasId.SubtypeId == hash)
                        if (gasItem.GasLevel >= 0.01f) return true;
            }
            else if (includeConsumables)
            {
                var consumable = item.Content as MyObjectBuilder_ConsumableItem;
                if (consumable != null)
                {
                    var definition = Survival.Find<MyConsumableItemDefinition>(consumable.SubtypeId);
                    if (definition?.Stats != null)
                        foreach (var itemStat in definition.Stats)
                            if (itemStat.Name == hash.String) return true;
                }
            }
            return false;
        }

        public static bool IsUsableItem(MyDefinitionBase item, MyDefinitionId stat)
        {
            var definition = item as MyOxygenContainerDefinition;
            if (definition != null)
                if (definition.StoredGasId.SubtypeId == stat.SubtypeId)
                    return true;
            var definition2 = item as MyConsumableItemDefinition;
            if (definition2 != null && definition2.Stats != null)
                foreach (var itemStat in definition2.Stats)
                    if (itemStat.Name == stat.SubtypeId.String) return true;
            return false;
        }

        private static float TakeFromItem(MyPhysicalInventoryItem item, MyStringHash stat, MyInventoryBase inventory, float need)
        {
            if (need < 0) return 0f;
            var gasItem = item.Content as MyObjectBuilder_OxygenContainerObject;
            if (gasItem == null) return need;
            var definition = Find<MyOxygenContainerDefinition>(gasItem.SubtypeId);
            if (definition != null)
            {
                if (definition.StoredGasId.SubtypeId != stat) return need;
                if (gasItem.GasLevel > 0)
                {
                    var thisUsed = Math.Min(need, gasItem.GasLevel * definition.Capacity);
                    gasItem.GasLevel -= thisUsed / definition.Capacity;
                    need -= thisUsed;
                    _inventoryUpdateNeeded = true;
                    return need;
                }
            }
            return need;
        }

        private static float ConsumeUsingItem(MyPhysicalInventoryItem item, MyStringHash stat, MyInventoryBase inventory, float need)
        {
            if (need < 0) return 0f;
            var consumable = item.Content as MyObjectBuilder_ConsumableItem;
            if (consumable == null) return need;

            var definition = Find<MyConsumableItemDefinition>(consumable.SubtypeId);
            if (definition?.Stats != null)
            {

                foreach (var itemStat in definition.Stats)
                {
                    if (itemStat.Name != stat.String) continue;
                    //MyAPIGateway.Utilities.ShowNotification($"packet out {itemStat.Name} == {stat.String}", 1000, MyFontEnum.Green);
                    var thisUsed = Math.Min(need, (float)item.Amount);
                    inventory.ConsumeItem(consumable.GetObjectId(), 10, inventory.Entity.EntityId);
                    need -= thisUsed;
                    _inventoryUpdateNeeded = true;
                    return 0;
                }
            }
            return need;
        }

        public delegate float HandleStatMethod(MyPhysicalInventoryItem item, MyStringHash stat, MyInventoryBase inventory, float input);

        public static readonly HandleStatMethod TakeAction = TakeFromItem;
        public static readonly HandleStatMethod ConsumeAction = ConsumeUsingItem;
        public int WaterBottles;
        public int FoodItems;

        public bool IsValid => Character != null &&
                               Hydration != null &&
                               Nutrition != null &&
                               Inventory != null;

#if AUTO_CONSUME_ITEMS
        bool UpdateNeed(int ticksToPass, ref int ticksPassed, MyEntityStat stat, float step, HandleStatMethod method1, HandleStatMethod method2)
        {
            ticksPassed++;
            if (ticksPassed <= ticksToPass) return false;
            HandleStat(stat, step, method1, method2);
            ticksPassed = 0;
            return true;
        }
#else
        bool UpdateNeed(int ticksToPass, ref float ticksPassed, MyEntityStat stat, float step, HandleStatMethod method, float ticks = 1f)
        {
            ticksPassed += ticks;
            if (ticksPassed <= ticksToPass) return false;
            HandleStat(stat, step, method);
            ticksPassed = 0;
            return true;
        }
#endif

        public enum MovementExertion
        {
            Idle = 0,
            Light = 1,
            Medium = 2,
            Heavy = 3,
        }


        public static MyObjectBuilder_Base SOLID_WASTE_OBJECT = new MyObjectBuilder_Ore() { SubtypeName = "SolidWaste" };
        public static MyObjectBuilder_Base LIQUID_WASTE_OBJECT = new MyObjectBuilder_Ore() { SubtypeName = "LiquidWaste" };

        IMyCryoChamber CryoChamber => Character.Parent as IMyCryoChamber;

        MyCockpit Cockpit => Character.Parent as MyCockpit;

        public string Name => Character != null ? Character.DisplayName : "unknown?";

        MovementExertion GetExertion()
        {
            switch (Character.CurrentMovementState)
            {
                case MyCharacterMovementEnum.Standing:
                case MyCharacterMovementEnum.Sitting:
                case MyCharacterMovementEnum.Crouching:
                case MyCharacterMovementEnum.Ladder:
                case MyCharacterMovementEnum.LadderOut:
                case MyCharacterMovementEnum.Flying:
                case MyCharacterMovementEnum.Died:
                case MyCharacterMovementEnum.RotatingLeft:
                case MyCharacterMovementEnum.RotatingRight:
                    return MovementExertion.Idle;
                case MyCharacterMovementEnum.Walking:
                case MyCharacterMovementEnum.BackWalking:
                case MyCharacterMovementEnum.WalkStrafingLeft:
                case MyCharacterMovementEnum.WalkStrafingRight:
                case MyCharacterMovementEnum.WalkingRightFront:
                case MyCharacterMovementEnum.WalkingRightBack:
                case MyCharacterMovementEnum.WalkingLeftFront:
                case MyCharacterMovementEnum.WalkingLeftBack:
                case MyCharacterMovementEnum.CrouchWalking:
                case MyCharacterMovementEnum.CrouchBackWalking:
                case MyCharacterMovementEnum.CrouchStrafingLeft:
                case MyCharacterMovementEnum.CrouchStrafingRight:
                case MyCharacterMovementEnum.CrouchWalkingRightFront:
                case MyCharacterMovementEnum.CrouchWalkingRightBack:
                case MyCharacterMovementEnum.CrouchWalkingLeftFront:
                case MyCharacterMovementEnum.CrouchWalkingLeftBack:
                case MyCharacterMovementEnum.CrouchRotatingLeft:
                case MyCharacterMovementEnum.CrouchRotatingRight:
                    return MovementExertion.Light;
                case MyCharacterMovementEnum.Running:
                case MyCharacterMovementEnum.Backrunning:
                case MyCharacterMovementEnum.RunStrafingLeft:
                case MyCharacterMovementEnum.RunStrafingRight:
                case MyCharacterMovementEnum.RunningRightFront:
                case MyCharacterMovementEnum.RunningRightBack:
                case MyCharacterMovementEnum.RunningLeftFront:
                case MyCharacterMovementEnum.RunningLeftBack:
                    return MovementExertion.Medium;
                case MyCharacterMovementEnum.Sprinting:
                case MyCharacterMovementEnum.Jump:
                case MyCharacterMovementEnum.Falling:
                case MyCharacterMovementEnum.LadderUp:
                case MyCharacterMovementEnum.LadderDown: 
                    return MovementExertion.Heavy;
                default:
                    return MovementExertion.Idle;
            }
        }

        float NutritionPassed()
        {
            var passed = 1f;
            if (Character.GetOutsideTemperature() < 0.25f)
                passed *= 2;
            switch (GetExertion())
            {
                case MovementExertion.Idle:
                    break;
                case MovementExertion.Light:
                    passed += 0.25f;
                    break;
                case MovementExertion.Medium:
                    passed += 0.75f;
                    break;
                case MovementExertion.Heavy:
                    passed += 2.5f;
                    break;
            }
            return passed;
        }

        float HydrationPassed()
        {
            var passed = 1f;
            if (Character.GetOutsideTemperature() > 0.75f)
                passed *= 2;
            switch (GetExertion())
            {
                case MovementExertion.Idle:
                case MovementExertion.Light:
                    break;
                case MovementExertion.Medium:
                    passed += 0.25f;
                    break;
                case MovementExertion.Heavy:
                    passed += 1f;
                    break;
            }
            return passed;
        }

        private int _radiationTickChange = 0;

        private const float RADIATION_LOG_SCALE = 1.3f;

        private int _lastFrameUpdate;

        void CalculateRadiation(int updates)
        {
            _radiationTicksPassed += updates;
            _radiationTickChange += updates;
            if (_radiationTicksPassed > RADIATION_CHECK_TICKS)
            {
                _radiationTicksPassed = 0;
                var radiationLevel = GetRadiationLevel();
                var radiation = Radiation.Value;
                if (radiationLevel > 0.25f)
                {
                    if (_radiationTickChange < RADIATION_ADD_TICKS)
                    {
                        return;
                    }

                    var delta = (float)(Math.Pow(radiationLevel * 10f, RADIATION_LOG_SCALE) / 10f);

                    _radiationTickChange = 0;
                    if (radiation < Radiation.MaxValue)
                        radiation += delta;
                }
                else
                {
                    if (_radiationTickChange < RADIATION_REMOVE_TICKS)
                    {
                        return;
                    }

                    _radiationTickChange = 0;
                    if (radiation > 0f)
                        radiation -= 1f;
                }

                if (radiation < 0)
                    radiation = 0f;

                if (radiation > Radiation.MaxValue)
                    radiation = Radiation.MaxValue;
                
                Radiation.Value = radiation;
            }
        }

        public void Simulate()
        {
            if (Character == null || Character.IsDead)
            {
                SurvivalSession.Remove(this);
                return;
            }

            if (!IsValid)
                return;

            if (CryoChamber != null && CryoChamber.IsWorking)
                return;


            var updates = MyAPIGateway.Session.GameplayFrameCounter - _lastFrameUpdate;
            _lastFrameUpdate = MyAPIGateway.Session.GameplayFrameCounter;

            if (UpdateNeed(NUTRITION_STEP_TICKS, ref _nutritionTicksPassed, Nutrition, updates, TakeAction, NutritionPassed()))
            {
                if (IsSatiated && Inventory != null)
                {
                    if (Inventory.AddItems(SOLID_WASTE_PER_STEP, SOLID_WASTE_OBJECT))
                        _inventoryUpdateNeeded = true;
                }
            }
            if (UpdateNeed(HYDRATION_STEP_TICKS, ref _hydrationTicksPassed, Hydration, updates, TakeAction, HydrationPassed()))
            {
                if (IsHydrated && Inventory != null)
                {
                    if (Inventory.AddItems(LIQUID_WASTE_PER_STEP, LIQUID_WASTE_OBJECT))
                    {
                        //MyAPIGateway.Utilities.ShowNotification("running!", 1, MyFontEnum.White);
                        _inventoryUpdateNeeded = true;
                    }
                }
            }

            CalculateRadiation(updates);

            if (_inventoryUpdateNeeded)
            {
                _inventoryUpdateNeeded = false;
                Inventory?.RaiseContentsChanged();
            }

            if (!Character.IsDead)
            {
                _dehydrationDamageTicks += updates;
                _starvationDamageTicks += updates;
                if (_dehydrationDamageTicks > ONCE_PER_TWO_SECONDS)
                {
                    _dehydrationDamageTicks = 0;
                    if (IsDehydrated)
                        Character.DoDamage(DEHYDRATION_DAMAGE_STEP, HydrationId, true);
                }
                if (_starvationDamageTicks > ONCE_PER_TWO_SECONDS)
                {
                    _starvationDamageTicks = 0;
                    if (IsStarving)
                        Character.DoDamage(STARVING_DAMAGE_STEP, NutritionId, true);
                }
                if (_radiationDamageTicks > ONCE_PER_SECOND * 30f)
                {
                    _radiationDamageTicks = 0;
                    if (IsIrradiated)
                        Character.DoDamage(RADIATION_DAMAGE_STEP, RadiationId, true);
                }
            }
            // Inventory?.AddItems(1, new MyObjectBuilder_Ore() { SubtypeName = "Ice" });
        }

        void HandleStat(MyEntityStat stat, float step, HandleStatMethod action1, HandleStatMethod action2 = null)
        {
            var nextValue = stat.Value;
            if (stat.Value > 0)
                nextValue -= step;

            var valueNeeded = stat.MaxValue - nextValue;
            if (valueNeeded > 0)
            {
                var items = Inventory?.GetItems();
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (valueNeeded <= 0) continue;
                        if (action1 != null) valueNeeded = action1(item, stat.StatId, Inventory, valueNeeded);
                        if (action2 != null) valueNeeded = action2(item, stat.StatId, Inventory, valueNeeded);
                    }
                }
            }
            stat.Value = stat.MaxValue - valueNeeded;
        }
    }

    public static class Survival
    {
        public static MyStringHash HydrationId = MyStringHash.GetOrCompute("Hydration");
        public static MyStringHash NutritionId = MyStringHash.GetOrCompute("Nutrition");
        public static MyStringHash RadiationId = MyStringHash.GetOrCompute("Radiation");

        public static readonly MyDefinitionId Hydration = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydration");

        /// <summary>
        /// Due to how SurvivalKit only uses 0.33 of the blueprint, there is often remainder leftover. This cleans up all inventory items.
        /// </summary>
        /// <param name="inventory"></param>
        public static void CleanInventory(MyInventory inventory)
        {
            var j = 0;
            while (j < inventory.ItemCount && j >= 0)
            {
                var itemCheck = inventory.GetItemByIndex(j);
                if (itemCheck.HasValue)
                {
                    var item = itemCheck.Value;
                    if (item.Content is MyObjectBuilder_ConsumableItem)
                    {
                        var remainder = (float)item.Amount % 1f;
                        if (remainder > 0f)
                            inventory.RemoveItems(item.ItemId, (MyFixedPoint)remainder);
                    }
                    if (item.Content is MyObjectBuilder_Ore)
                    {
                        var remainder = (float)item.Amount % 0.01f;
                        if (remainder > 0f)
                            inventory.RemoveItems(item.ItemId, (MyFixedPoint)remainder);
                    }
                }
                j++;
            }
        }

        private static Dictionary<MyStringHash, float> _voxelMaterialLookup =
            new Dictionary<MyStringHash, float>()
            {
                { MyStringHash.GetOrCompute("Grass"), 0.8f },
                { MyStringHash.GetOrCompute("Soil"), 0.8f },
                { MyStringHash.GetOrCompute("AlienGreenGrass"), 0.8f },

                { MyStringHash.GetOrCompute("GrassOld"), 0.7f },
                { MyStringHash.GetOrCompute("GrassDry"), 0.7f },
                { MyStringHash.GetOrCompute("OrangeAlienGrass"), 0.6f },
                { MyStringHash.GetOrCompute("AlienYellowGrass"), 0.6f },
                { MyStringHash.GetOrCompute("Soildry"), 0.6f },

                { MyStringHash.GetOrCompute("MarsSoil"), 0.4f },

                { MyStringHash.GetOrCompute("Sand"), 0.3f },
                { MyStringHash.GetOrCompute("Rock"), 0.6f },

                { MyStringHash.GetOrCompute("Snow"), 0.3f },
                { MyStringHash.GetOrCompute("Ice"), 0.3f },

                { MyStringHash.GetOrCompute("MoonSoil"), 0.0f },
            };

        public static float GetHumidity(IMyTerminalBlock terminalBlock)
        {
            var position = terminalBlock.PositionComp.GetPosition();
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null) return 0;
            if (planet.HasAtmosphere && MySurvivalCharacter.InAtmosphere(planet, position))
            {
                var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
                var material = planet.GetMaterialAt(ref surfacePoint);
                if (material != null)
                {
                    var humidity = 0f;
                    _voxelMaterialLookup.TryGetValue(material.MaterialTypeNameHash, out humidity);
                    return humidity;
                }
            }
            return 0f;
        }

        public static float GetAirDensity(IMyTerminalBlock terminalBlock)
        {
            var position = terminalBlock.PositionComp.GetPosition();
            var myCubeGrid = terminalBlock.CubeGrid;
            if (myCubeGrid != null)
            {
                // Easy check once we know which block to query the grid for.
                var oxygenBlock = myCubeGrid?.GasSystem?.GetOxygenBlock(position);
                if (oxygenBlock?.Room != null)
                {
                    if (oxygenBlock.Room.IsAirtight)
                        return oxygenBlock.Room.OxygenLevel(myCubeGrid.GridSize);
                }
            }
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null) return 0;
            if (planet.HasAtmosphere && MySurvivalCharacter.InAtmosphere(planet, position))
                return planet.GetAirDensity(position);
            return 0f;
        }

        public static float GetOxygenLevel(IMyTerminalBlock terminalBlock)
        {
            var position = terminalBlock.PositionComp.GetPosition();
            var myCubeGrid = terminalBlock.CubeGrid;
            if (myCubeGrid != null)
            {
                // Easy check once we know which block to query the grid for.
                var oxygenBlock = myCubeGrid?.GasSystem?.GetOxygenBlock(position);
                if (oxygenBlock?.Room != null)
                {
                    if (oxygenBlock.Room.IsAirtight)
                        return oxygenBlock.Room.OxygenLevel(myCubeGrid.GridSize);
                }
            }
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null) return 0;
            if (planet.HasAtmosphere && MySurvivalCharacter.InAtmosphere(planet, position))
                return planet.GetOxygenForPosition(position) * planet.GetAirDensity(position);
            return 0f;
        }

        public static float GetOxygenLevel(BoundingBoxD worldAabb, Vector3D position)
        {
            // Get a list of entities near the character.
            var result = new List<MyEntity>();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref worldAabb, result);
            foreach (MyEntity myEntity in result)
            {
                var myCubeGrid = myEntity as IMyCubeGrid;
                if (myCubeGrid != null)
                {
                    // Easy check once we know which block to query the grid for.
                    var gridPos = myCubeGrid.WorldToGridInteger(position);
                    var oxygenBlock = myCubeGrid.GasSystem.GetOxygenBlock(gridPos);
                    if (oxygenBlock.Room != null)
                    {
                        if (oxygenBlock.Room.IsAirtight)
                            return oxygenBlock.Room.OxygenLevel(myCubeGrid.GridSize);
                        return oxygenBlock.Room.EnvironmentOxygen;
                    }
                }
            }
            return 0f;
        }

        public const float PLANET_CRUST = 30f;

        public static bool DoSimulation
        {
            get
            {
                if (MyAPIGateway.Session == null)
                    return false;
                return MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE ||
                       MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Utilities.IsDedicated;
            }
        }

        public static IMyTerminalControlOnOffSwitch RefreshToggle;

        public static void UpdateTerminal(IMyTerminalBlock block)
        {
            if (!GetRefreshToggle())
                return;

            RefreshTerminalControls(block);
        }

        public static bool GetRefreshToggle()
        {

            List<IMyTerminalControl> items;
            MyAPIGateway.TerminalControls.GetControls<IMyTerminalBlock>(out items);

            foreach (var item in items)
            {

                if (item.Id == "ShowInToolbarConfig")
                {
                    RefreshToggle = (IMyTerminalControlOnOffSwitch)item;
                    break;
                }
            }
            return RefreshToggle != null;
        }

        //forces GUI refresh
        public static void RefreshTerminalControls(IMyTerminalBlock b)
        {
            if (RefreshToggle != null)
            {

                var originalSetting = RefreshToggle.Getter(b);
                RefreshToggle.Setter(b, !originalSetting);
                RefreshToggle.Setter(b, originalSetting);
            }
        }

        public static float GetRadiationAt(Vector3D position)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null) return 1;

            float naturalGravityInterference;
            MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out naturalGravityInterference);

            var artificialGravityFactor =
                MyAPIGateway.Physics.CalculateArtificialGravityAt(position, naturalGravityInterference).Length() / 9.8f;

            var gravityFactor = naturalGravityInterference + artificialGravityFactor * 0.5f;
            if (gravityFactor > 1f)
                gravityFactor = 1f;

            var radiation = 1 - (float)Math.Pow(gravityFactor, 3f);

            if (planet.HasAtmosphere && MySurvivalCharacter.InAtmosphere(planet, position))
            {
                var airDensity = (float)Math.Pow(planet.GetAirDensity(position), 0.5f);
                if (airDensity > 1f)
                    airDensity = 1f;
                radiation *= 1f - airDensity;
            }

            var surfacePoint = planet.GetClosestSurfacePointGlobal(position);
            var planetCenter = planet.PositionComp.WorldAABB.Center;

            var planetCrustSqr = Vector3.DistanceSquared(surfacePoint, planetCenter);
            var heightSqr = Vector3.DistanceSquared(position, planetCenter);

            var delta = planetCrustSqr - heightSqr;

            // below surface ratio
            if (delta > 0)
            {
                // below surface
                var depth = Math.Min(Vector3.Distance(position, surfacePoint), PLANET_CRUST);
                var ratio = 1f - (depth / PLANET_CRUST);
                radiation *= ratio;
            }

            if (radiation < 0)
                radiation = 0f;
            //MyAPIGateway.Utilities.ShowNotification($"radiation {radiation:F2}", 10, MyFontEnum.Green);
            return radiation;
        }

        /// <summary>
        /// returns the number of ticks between each nutrition step
        /// </summary>
        public const int NUTRITION_STEP_TICKS = (int)((TICKS_PER_MINUTE * 80) / 100);
        /// <summary>
        /// returns the number of ticks between each hydration step
        /// </summary>
        public const int HYDRATION_STEP_TICKS = (int)((TICKS_PER_MINUTE * 40) / 100);

        /// <summary>
        /// returns the number of ticks between each radiation step
        /// </summary>
        public const float RADIATION_CHECK_TICKS = TICKS_PER_SECOND * 1f;
        public const float RADIATION_ADD_TICKS = TICKS_PER_HOUR * 2f / 100f;
        public const float RADIATION_REMOVE_TICKS = TICKS_PER_SECOND * 3f;


        public const int ONCE_PER_TWO_SECONDS = TICKS_PER_SECOND * 2;
        public const int ONCE_PER_SECOND = TICKS_PER_SECOND;
        public const int TICKS_PER_SECOND = 60;
        public const int TICKS_PER_MINUTE = TICKS_PER_SECOND * 60;
        public const int TICKS_PER_HOUR = TICKS_PER_MINUTE * 60;

        /// <summary>
        /// how far will be moved with each hydration step
        /// recommended to not be below one to prevent changes that wouldnt be visible
        /// </summary>
        public const float HYDRATION_STEP = 1;

        /// <summary>
        /// how far will be moved with each hydration step
        /// recommended to not be below one to prevent changes that wouldnt be visible
        /// </summary>
        public const float RADIATION_STEP = 1;

        /// <summary>
        /// how far will be moved with each nutrition step
        /// recommended to not be below one to prevent changes that wouldnt be visible
        /// </summary>
        public const float NUTRITION_STEP = 1;

        /// <summary>
        /// how much waste to generate per nutrition step, based on 40 organics
        /// </summary>
        public static MyFixedPoint SOLID_WASTE_PER_STEP = (MyFixedPoint)(2f / 100f);
        public static MyFixedPoint LIQUID_WASTE_PER_STEP = (MyFixedPoint)(1f / 100f);


        public const int DEHYDRATION_DAMAGE_STEP = 2;
        public const int STARVING_DAMAGE_STEP = 1;
        public const int RADIATION_DAMAGE_STEP = 1;

        public const int FORCE_PLAYER_UPDATE_TICKS = TICKS_PER_SECOND * 10;

        public static Dictionary<MyStringHash, MyDefinitionBase> DefintionsLookup =
            new Dictionary<MyStringHash, MyDefinitionBase>();

        public static void CacheDefintions()
        {
            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var consumable = definition as MyConsumableItemDefinition;
                if (consumable != null && consumable.Stats != null)
                {
                    DefintionsLookup.Add(consumable.Id.SubtypeId, consumable);
                    continue;
                }

                var tank = definition as MyOxygenContainerDefinition;
                if (tank != null)
                {
                    DefintionsLookup.Add(tank.Id.SubtypeId, tank);
                    continue;
                }
            }
        }

        private static MyDefinitionBase _nullDefinition = null;

        public static T Find<T>(MyStringHash subTypeId)
        {
            MyDefinitionBase result;
            DefintionsLookup.TryGetValue(subTypeId, out result);
            if (result is T)
            {
                return (T)(object)result;
            }
            return (T)(object)_nullDefinition;
        }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class SurvivalSession : MySessionComponentBase
    {
        public static MySurvivalCharacter MyCharacter;

        public static List<IMyPlayer> Players = new List<IMyPlayer>();

        public static List<MySurvivalCharacter> Characters = new List<MySurvivalCharacter>();

        public static MySurvivalCharacter Find(IMyEntity entity)
        {
            if (entity == null) return null;
            foreach (var survivalCharacter in Characters)
            {
                if (survivalCharacter?.Character?.EntityId == entity.EntityId) 
                    return survivalCharacter;
            }
            return null;
        }

        private static int _ticksSincePlayerCheck = int.MaxValue;

        private static bool _initialized;
        
        void InitializeManager()
        {
            if (!_initialized)
            {
                if (MyAPIGateway.Session == null)
                    return;

                if (MyAPIGateway.Multiplayer == null && MyAPIGateway.Session.OnlineMode != MyOnlineModeEnum.OFFLINE)
                    return;

                if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session?.Camera == null)
                    return;

                if (MyDefinitionManager.Static == null)
                    return;

                CacheDefintions();
                _initialized = true;
            }
            MyAPIGateway.Utilities.InvokeOnGameThread(PopulateCharacters, "VSE");
            MyVisualScriptLogicProvider.PlayerDied += OnPlayerDied;
            MyVisualScriptLogicProvider.PlayerSpawned += OnPlayerSpawned;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;

            LogServer("Initializing");
            if (MyAPIGateway.Utilities.IsDedicated)
                LogServer("is dedicated server");
            LogServer($"Nutrition every {NUTRITION_STEP_TICKS} ticks");
            LogServer($"Hydration every {HYDRATION_STEP_TICKS} ticks");
        }

        private static void OnEntityAdd(IMyEntity entity)
        {
            var character = entity as IMyCharacter;
            if (character != null)
                Register(character);

            var cockpit = entity as IMyCockpit;
            if (cockpit != null)
                Register(cockpit.Pilot);

            var turret = entity as IMyTurretControlBlock;
            if (turret != null)
                Register(turret.Pilot);

            var remoteControl = entity as IMyRemoteControl;
            if (remoteControl != null)
                Register(remoteControl.Pilot);

            var entityChildren = new List<IMyEntity>();
            entity.GetChildren(entityChildren, IsCharacter);
            foreach (var child in entityChildren)
                OnEntityAdd(child);
        }


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            InitializeManager();
            base.Init(sessionComponent);
        }

        public static void SpawnLoadout(IMyCharacter character)
        {
            if (character == null) return;
            character.GetInventory().AddItems(1, new MyObjectBuilder_Datapad()
            {
                SubtypeName = "Datapad",
                Name = "Test",
                Data = "Test Data",
            });
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            // dedicated server is much simpler
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                if (MyAPIGateway.Multiplayer == null)
                    return;
                SimulateForPlayers();
                return;
            }

            // if the local player has a character, we need to find it
            if (MyCharacter?.IsDead ?? true)
            {
                MyCharacter = null;
            }

            if (MyCharacter == null)
            {
                var foundCharacter = MyAPIGateway.Session.Player?.Character;
                if (foundCharacter != null)
                {
                    MyCharacter = new MySurvivalCharacter(foundCharacter, true);
                }
            }

            // handle single player very simply
            if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
            {
                MyCharacter?.Simulate();
            }
            else if (MyAPIGateway.Multiplayer?.IsServer ?? false)
            {
                //PopulatePlayers();
                SimulateForPlayers();
            }

            MyCharacter?.Draw();
        }


        private void SimulateForPlayers()
        {
            for (var i = Characters.Count - 1; i >= 0; i--)
            {
                var character = Characters[i];
                character.Simulate();
            }
        }

        public static void LogServer(String logMessage)
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                MyLog.Default.WriteLineAndConsole($"VSE: {logMessage}");
            }
            else
            {
                MyLog.Default.WriteLine($"VSE: {logMessage}");
                //MyVisualScriptLogicProvider.SendChatMessage($"VSE: {logMessage}");
            }

        }

        private static int _lastPlayerCount = int.MinValue;

        private static bool IsCharacter(IMyEntity entity)
        {
            return entity is IMyCharacter;
        }

        private static void PopulateCharacters()
        {
            if (!DoSimulation) return;

            var entities = MyEntities.GetEntities();
            //MyAPIGateway.Entities.GetEntities(entities, IsCharacter);
            LogServer($"checking for characters!");
            foreach (var entity in entities)
            {
                //LogServer($"found {entity.DisplayName}");
                OnEntityAdd(entity);
            }
        }
        

        static void Register(IMyCharacter character)
        {
            var foundCharacter = Find(character);
            if (foundCharacter == null || foundCharacter.IsDead)
                new MySurvivalCharacter(character);
        }

        private static void OnPlayerDied(long playerid)
        {
            var entityId = MyVisualScriptLogicProvider.GetPlayersEntityId(playerid);
            var entity = MyVisualScriptLogicProvider.GetEntityById(entityId) as IMyCharacter;
            var character = Find(entity);
            if (character != null)
                Remove(character);
        }

        private static void OnPlayerSpawned(long playerid)
        {
            var entityId = MyVisualScriptLogicProvider.GetPlayersEntityId(playerid);
            var entity = MyVisualScriptLogicProvider.GetEntityById(entityId) as IMyCharacter;
            if (entity != null)
                SurvivalSession.Register(entity);
        }

        /*private void PopulatePlayers()
        {
            return;
            if (Players.Count == _lastPlayerCount && _ticksSincePlayerCheck < FORCE_PLAYER_UPDATE_TICKS)
            {
                _ticksSincePlayerCheck++;
                return;
            }

            _ticksSincePlayerCheck = 0;

            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players);

            // set as old
            foreach (var character in Characters)
            {
                character.IsDirty = true;
            }

            // populate
            foreach (var player in Players)
            {
                //var entity = GetPlayerEntity(player);
                var entity = player.Character;
                if (entity == null) continue;
                var character = Find(entity);
                if (character != null)
                {
                    character.IsDirty = false;
                    LogServer($"found {character.Character.DisplayName}");
                    //MyAPIGateway.Utilities.ShowNotification($"found character {character.Character.DisplayName}!", ONCE_PER_SECOND, MyFontEnum.Green);
                }
                else
                {
                    //MyAPIGateway.Utilities.ShowNotification($"new character {entity.DisplayName}!", ONCE_PER_SECOND, MyFontEnum.Green);
                    new MySurvivalCharacter((IMyCharacter)entity);
                }
            }

            // clear out old
            for (var i = 0; i < Characters.Count; i++)
            {
                if (Characters[i].IsDirty)
                {
                    LogServer($"removing expired character {Characters[i].Name} at {i}");
                    Characters.RemoveAt(i);
                }
            }

            _lastPlayerCount = Players.Count;
        }*/

        protected override void UnloadData()
        {
            base.UnloadData();
            Players.Clear();
            Characters.Clear();
            DefintionsLookup.Clear();
            _ticksSincePlayerCheck = int.MaxValue;
            _initialized = false;
            _lastPlayerCount = int.MinValue;
            MyVisualScriptLogicProvider.PlayerSpawned -= OnPlayerSpawned;
            MyVisualScriptLogicProvider.PlayerDied -= OnPlayerDied;
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
        }

        public static void Remove(MySurvivalCharacter character)
        {
            LogServer($"removing expired character {character.Name}");
            for (var i = Characters.Count - 1; i >= 0; i--)
            {
                var survivalCharacter = Characters[i];
                if (survivalCharacter.IsDead || survivalCharacter.Character.EntityId == character.Character.EntityId)
                {
                    Characters.RemoveAt(i);
                    if (MyCharacter != null && MyCharacter.IsDead)
                    {
                        MyCharacter = null;
                    }
                }
            }
        }
    }

    public class NutritionHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (SurvivalSession.MyCharacter == null) return;
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 1f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public NutritionHudStat()
        {
            Id = MyStringHash.GetOrCompute("player_nutrition");
        }

        public MyStringHash Id { get; private set; }

        public float CurrentValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Nutrition == null) return 0;
                return SurvivalSession.MyCharacter.Nutrition.Value;
            }
        }

        public float MaxValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Nutrition == null) return 0;
                return SurvivalSession.MyCharacter.Nutrition.MaxValue;
            }
        }

        public float MinValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Nutrition == null) return 0;
                return SurvivalSession.MyCharacter.Nutrition.MinValue;
            }
        }
    }

    public class EnvironmentRadiationHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (SurvivalSession.MyCharacter == null) return;
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 0.5f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public EnvironmentRadiationHudStat()
        {
            Id = MyStringHash.GetOrCompute("environment_radiation_level");
        }

        public MyStringHash Id { get; private set; }

        private static float _value;
        public float CurrentValue => _value;

        public float MaxValue => 100f;

        public float MinValue => 0f;

        public static void SetValue(float f)
        {
            _value = f;
        }
    }

    public class RadiationHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (SurvivalSession.MyCharacter == null) return;
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 0.5f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public RadiationHudStat()
        {
            Id = MyStringHash.GetOrCompute("player_radiation");
        }

        public MyStringHash Id { get; private set; }

        public float CurrentValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Radiation == null) return 0;
                return SurvivalSession.MyCharacter.Radiation.Value;
            }
        }

        public float MaxValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Radiation == null) return 0;
                return SurvivalSession.MyCharacter.Radiation.MaxValue;
            }
        }

        public float MinValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Radiation == null) return 0;
                return SurvivalSession.MyCharacter.Radiation.MinValue;
            }
        }
    }

    public class HydrationHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (SurvivalSession.MyCharacter == null) return;
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 1f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public HydrationHudStat()
        {
            Id = MyStringHash.GetOrCompute("player_hydration");
        }

        public MyStringHash Id { get; private set; }

        public float CurrentValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Hydration == null) return 0;
                return SurvivalSession.MyCharacter.Hydration.Value;
            }
        }

        public float MaxValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Hydration == null) return 0;
                return SurvivalSession.MyCharacter.Hydration.MaxValue;
            }
        }

        public float MinValue
        {
            get
            {
                if (SurvivalSession.MyCharacter?.Hydration == null) return 0;
                return SurvivalSession.MyCharacter.Hydration.MinValue;
            }
        }
    }

    public class WaterBottleHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 1f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public WaterBottleHudStat()
        {
            Id = MyStringHash.GetOrCompute("player_hydration_bottles");
        }

        public MyStringHash Id { get; private set; }

        public float CurrentValue
        {
            get
            {
                if (SurvivalSession.MyCharacter == null) return 0;
                return SurvivalSession.MyCharacter.WaterBottles;
            }
        }

        public float MaxValue => 1;
        public float MinValue => 0;
    }

    public class FoodItemsHudStat : IMyHudStat
    {
        private float _lastUpdatedValue = float.NegativeInfinity;
        public void Update()
        {
            if (Math.Abs(CurrentValue - _lastUpdatedValue) >= 1f)
            {
                _lastUpdatedValue = CurrentValue;
                _cachedString = CurrentValue.ToString("##0");
            }
        }

        public string GetValueString() => _cachedString;

        private String _cachedString = "0";

        public FoodItemsHudStat()
        {
            Id = MyStringHash.GetOrCompute("player_food_items");
        }

        public MyStringHash Id { get; private set; }

        public float CurrentValue
        {
            get
            {
                if (SurvivalSession.MyCharacter == null) return 0;
                return SurvivalSession.MyCharacter.FoodItems;
            }
        }

        public float MaxValue => 1;
        public float MinValue => 0;
    }
}
