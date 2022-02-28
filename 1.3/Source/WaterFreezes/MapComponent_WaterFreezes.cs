﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using System.Runtime.CompilerServices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WF
{
    public class MapComponent_WaterFreezes : MapComponent
    {
		public bool Initialized;
        public TerrainDef[] NaturalWaterTerrainGrid;
		public TerrainDef[] AllWaterTerrainGrid;
		public float[] IceDepthGrid;
		public float[] WaterDepthGrid;
		public float[] PseudoWaterElevationGrid;
		//Ice thresholds of type by depth.
		public float ThresholdThinIce = .15f; //This is ratio of ice to water, unlike other thresholds.
		public float ThresholdIce = 50;
		public float ThresholdThickIce = 110;

		public MapComponent_WaterFreezes(Map map) : base(map)
		{
			WaterFreezes.Log("New MapComponent constructed (for map " + map.uniqueID + ") adding it to the cache.");
			WaterFreezesCompCache.SetFor(map, this);
		}

        public override void MapGenerated()
		{
			Initialize();
		}

        public override void MapRemoved()
        {
			WaterFreezes.Log("Removing MapComponent from cache due to map removal (for map " + map.uniqueID + ").");
			WaterFreezesCompCache.compCachePerMap.Remove(map.uniqueID); //Yeet from cache so it can die in the GC.
        }

        public void Initialize()
		{
			WaterFreezes.Log("MapComponent Initializing (for map uniqueId " + map.uniqueID + "\")..");
			if (WaterDepthGrid == null) //If we have no water depth grid..
			{
				WaterFreezes.Log("Instantiating water depth grid..");
				WaterDepthGrid = new float[map.cellIndices.NumGridCells]; //Instantiate it.
			}
			if (NaturalWaterTerrainGrid == null) //If we haven't got a waterGrid loaded from the save file, make one.
			{
				WaterFreezes.Log("Generating natural water grid and populating water depth grid..");
				NaturalWaterTerrainGrid = new TerrainDef[map.cellIndices.NumGridCells];
				for (int i = 0; i < map.cellIndices.NumGridCells; i++)
				{
					var currentTerrain = map.terrainGrid.TerrainAt(i);
					if (currentTerrain.IsFreezableWater())
					{
						NaturalWaterTerrainGrid[i] = currentTerrain;
						WaterDepthGrid[i] = WaterFreezesStatCache.GetExtension(currentTerrain).MaxWaterDepth;
					}
					else if (currentTerrain.IsBridge())
                    {
						var underTerrain = map.terrainGrid.UnderTerrainAt(i);
						if (underTerrain.IsFreezableWater())
						{
							NaturalWaterTerrainGrid[i] = underTerrain; 
							WaterDepthGrid[i] = WaterFreezesStatCache.GetExtension(underTerrain).MaxWaterDepth;
						}
                    }
				}
			}
			if (AllWaterTerrainGrid == null) //If we have no all-water terrain grid..
			{
				WaterFreezes.Log("Cloning natural water grid into all water grid..");
				AllWaterTerrainGrid = (TerrainDef[])NaturalWaterTerrainGrid.Clone(); //Instantiate it to content of the natural water array for starters.
			}
			if (IceDepthGrid == null)
			{
				WaterFreezes.Log("Instantiating ice depth grid..");
				IceDepthGrid = new float[map.cellIndices.NumGridCells];
			}
			if (PseudoWaterElevationGrid == null)
			{
				PseudoWaterElevationGrid = new float[map.cellIndices.NumGridCells];
				UpdatePseudoWaterElevationGrid();
			}
			Initialized = true;
		}

		public override void MapComponentTick()
		{
			if (!Initialized) //If we aren't initialized..
				Initialize(); //Initialize it!
			if (Find.TickManager.TicksGame % WaterFreezesSettings.IceRate != 0) //If it's not once per hour..
				return; //Don't execute the rest, throttling measure.
			for (int i = 0; i < AllWaterTerrainGrid.Length; i++) //Thread this later probably.
			{
				var cell = map.cellIndices.IndexToCell(i);
				var water = AllWaterTerrainGrid[i];
				if (water != null) //If it's water we track..
				{
					var extension = WaterFreezesStatCache.GetExtension(water);
					UpdateIceForTemperature(cell, extension);
					UpdateIceStage(cell, extension);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdatePseudoWaterElevationGrid()
        {
			WaterFreezes.Log("Updating pseudo water elevation grid..");
			for (int i = 0; i < AllWaterTerrainGrid.Length; i++)
				if (AllWaterTerrainGrid[i] != null)
					UpdatePseudoWaterElevationGridForCell(map.cellIndices.IndexToCell(i));
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdatePseudoWaterElevationGridForCell(IntVec3 cell)
		{
			int i = map.cellIndices.CellToIndex(cell);
			var adjacentCells = GenAdjFast.AdjacentCells8Way(cell);
			float pseudoElevationScore = 0;
			for (int j = 0; j < adjacentCells.Count; j++)
			{
				int adjacentCellIndex = map.cellIndices.CellToIndex(adjacentCells[j]);
				if (adjacentCellIndex < 0 || adjacentCellIndex >= map.terrainGrid.topGrid.Length) //If it's a negative index or it's a larger index than the map's grid length (faster to get topGrid.Length than use property on the cellIndices).
					continue; //Skip it.
				if (AllWaterTerrainGrid[adjacentCellIndex] == null) //If it's land (e.g., not recognized water)..
					pseudoElevationScore += 1; //+1 for each land
			}
			PseudoWaterElevationGrid[i] = pseudoElevationScore;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdatePseudoWaterElevationGridAtAndAroundCell(IntVec3 cell)
		{
			int i = map.cellIndices.CellToIndex(cell);
			var adjacentCells = GenAdjFast.AdjacentCells8Way(cell);
			float pseudoElevationScore = 0;
			for (int j = 0; j < adjacentCells.Count; j++)
			{
				int adjacentCellIndex = map.cellIndices.CellToIndex(adjacentCells[j]);
				if (adjacentCellIndex < 0 || adjacentCellIndex >= map.terrainGrid.topGrid.Length) //If it's a negative index or it's a larger index than the map's grid length (faster to get topGrid.Length than use property on the cellIndices).
					continue; //Skip it.
				if (AllWaterTerrainGrid[adjacentCellIndex] == null) //If it's land (e.g., not recognized water)..
					pseudoElevationScore += 1; //+1 for each land
				else
					UpdatePseudoWaterElevationGridForCell(cell); //Update for this cell as well.
			}
			PseudoWaterElevationGrid[i] = pseudoElevationScore;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TerrainExtension_WaterStats SetMaxWaterByDef(int i, TerrainDef water = null, bool updateIceStage = true)
		{
			if (water == null) //Was not passed in..
				water = AllWaterTerrainGrid[i]; //Get it.
			var extension = WaterFreezesStatCache.GetExtension(water);
			WaterDepthGrid[i] = extension.MaxWaterDepth;
			if (updateIceStage)
				UpdateIceStage(map.cellIndices.IndexToCell(i));
			return extension;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateIceForTemperature(IntVec3 cell, TerrainExtension_WaterStats extension = null)
		{
			var temperature = GenTemperature.GetTemperatureForCell(cell, map);
			int i = map.cellIndices.CellToIndex(cell);
			var water = AllWaterTerrainGrid[i];
			if (extension == null) //If it wasn't passed in..
				extension = WaterFreezesStatCache.GetExtension(water); //Get it.
			temperature += extension.FreezingPointOffset; //Offset freezing point by the configured amount (used for things like making rivers and oceans less easily frozen).
			if (temperature == 0) //If it's 0degC..
				return; //We don't update when ambiguous.
			var currentIce = IceDepthGrid[i];
			var currentWater = WaterDepthGrid[i];
			if (temperature < 0) //Temperature is below zero..
			{
				if (currentWater > 0 && currentIce < extension.MaxIceDepth)
				{
					var elevation = PseudoWaterElevationGrid[i];
                    #region Only Allow Cells With Adjacent Land Or Ice To Freeze
                    if (elevation == 0) //if this isn't one of the edge cells..
					{
						//If none of the adjacent cells have ice..
						var adjacentCells = GenAdjFast.AdjacentCells8Way(cell);
						bool foundIce = false;
						for (int j = 0; j < adjacentCells.Count; ++j)
                        {
							var adjacentCell = adjacentCells[j];
							int adjacentCellIndex = map.cellIndices.CellToIndex(adjacentCell);
							if (adjacentCellIndex < 0 || adjacentCellIndex >= map.terrainGrid.topGrid.Length) //If it's a negative index or it's a larger index than the map's grid length (faster to get topGrid.Length than use property on the cellIndices).
								continue;
							var adjacentCellTerrain = map.terrainGrid.TerrainAt(adjacentCellIndex);
							if (adjacentCellTerrain.IsThawableIce())
                            {
								foundIce = true;
								break;
                            }
						}
						if (!foundIce)
							return; //We aren't going to freeze before there's ice adjacent to us.
					}
                    #endregion
                    var change = -temperature //Based on negated temperature..
						* (WaterFreezesSettings.FreezingFactor + elevation) //But sped up by a multiplier which takes into account surrounding terrain.
						/ 2500f * WaterFreezesSettings.IceRate; //Adjust to iceRate based on the 2500 we tuned it to originally.
					if ((WaterDepthGrid[i] -= change) < 0) //Water depth goes down.. if that value is less than zero now, then..
						WaterDepthGrid[i] = 0; //Set it back to zero.
					//Ice depth goes up (by change or the remaining water if smaller).. if that value is greater than supported, then..
					if ((IceDepthGrid[i] += (change < currentWater ? change : currentWater)) > extension.MaxIceDepth) 
						IceDepthGrid[i] = extension.MaxIceDepth;
				}
			}
			else if (temperature > 0) //Temperature is above zero..
			{
				if (currentIce > 0)// && currentWater < extension.MaxWaterDepth) This is the parallel check to the above one, but this shouldn't matter and we should probably let ice keep melting either way.
				{
					var change = temperature //Based on temperature..
						/ (WaterFreezesSettings.ThawingFactor - PseudoWaterElevationGrid[i]) / //But slowed down by a divisor which takes into account surrounding terrain.
						(currentIce / 100f) //Slow thawing further by ice thickness per 100 ice.
						/ 2500f * WaterFreezesSettings.IceRate; //Adjust to iceRate based on the 2500 we tuned it to originally.
					if ((IceDepthGrid[i] -= change) < 0) //Ice goes down.. if that value is less than zero now, then..
						IceDepthGrid[i] = 0; //Set it back to zero.
					//Water depth goes up (by change or the remaining ice if smaller).. if that value is greater than supported, then..
					if ((WaterDepthGrid[i] += (change < currentIce ? change : currentIce)) > extension.MaxWaterDepth) 
						WaterDepthGrid[i] = extension.MaxWaterDepth; //Cap it.
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateIceStage(IntVec3 cell, TerrainExtension_WaterStats extension = null, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			int i = map.cellIndices.CellToIndex(cell);
			float iceDepth = IceDepthGrid[i];
			float waterDepth = WaterDepthGrid[i];
			var water = AllWaterTerrainGrid[i];
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			var appropriateTerrain = GetAppropriateTerrainFor(water, waterDepth, iceDepth, extension);
			if (currentTerrain.IsBridge() || (TerrainSystemOverhaul_Interop.TerrainSystemOverhaulPresent && TerrainSystemOverhaul_Interop.GetBridge(map.terrainGrid, cell) != null)) //If it's a bridge..
			{
				//We deal with underTerrain.
				if (underTerrain == null) //If it wasn't passed in..
					underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
				if (underTerrain != appropriateTerrain)
					map.terrainGrid.SetUnderTerrain(cell, appropriateTerrain);
				else
					CheckAndRefillCell(cell, extension);
			}
			else //Not a bridge..
			{ 
				//We deal with regular terrain.
				if (currentTerrain != appropriateTerrain)
					map.terrainGrid.SetTerrain(cell, appropriateTerrain);
				else
					CheckAndRefillCell(cell, extension);
			}
			BreakdownOrDestroyBuildingsInCellIfInvalid(cell);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TerrainDef GetAppropriateTerrainFor(TerrainDef waterTerrain, float waterDepth, float iceDepth, TerrainExtension_WaterStats extension = null)
		{
			var percentIce = iceDepth / (iceDepth + waterDepth);
			if (float.IsNaN(percentIce) || percentIce < ThresholdThinIce) //If there's no meaningful amount of ice.. (the IsNaN is for the case where 0/0)
				return waterTerrain;
			else if (iceDepth < ThresholdIce) //If there's ice, but it's below the regular ice depth threshold..
				return extension.ThinIceDef;
			else if (iceDepth < ThresholdThickIce) //If it's between regular ice and thick ice in depth..
				return extension.IceDef;
			else //Only thick left..
				return extension.ThickIceDef;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void CheckAndRefillCell(IntVec3 cell, TerrainExtension_WaterStats extension = null)
		{
			if (cell.GetTemperature(map) <= 0) //If it's at or below freezing..
				return; //Do not thaw!
			int i = map.cellIndices.CellToIndex(cell);
			var naturalWater = NaturalWaterTerrainGrid[i];
			if (naturalWater != null) //If it's natural water..
			{
				var season = GenLocalDate.Season(map);
				if (season == Season.Spring || season == Season.Summer || season == Season.PermanentSummer) //If it's the right season..
				{
					if (extension == null)
						extension = WaterFreezesStatCache.GetExtension(naturalWater);
					//Refill the cell..
					if (WaterDepthGrid[i] < extension.MaxWaterDepth) //If it's not over-full..
						WaterDepthGrid[i] += 1f / 2500f * WaterFreezesSettings.IceRate; //Fill
					if (WaterDepthGrid[i] > extension.MaxWaterDepth) //If it's too full..
						WaterDepthGrid[i] = extension.MaxWaterDepth; //Cap it.
				}
			}
		}

		public List<string> BreakdownOrDestroyExceptedDefNames = new()
		{
			"Shuttle",
			"ShuttleCrashed",
		};

		public List<string> BreakdownOrDestroyExceptedPlaceWorkerTypeStrings = new()
		{
			"RimWorld.PlaceWorker_Conduit",
		};

		public List<string> BreakdownOrDestroyExceptedPlaceWorkerFailureReasons = new()
		{
			"VPE_NeedsDistance".Translate(), //If it's a tidal generator trying to see if it's too close to itself..
			"WFFT_NeedsDistance".Translate(), //If it's a fish trap or fish net trying to see if it's too close to itself..
		};

		public void BreakdownOrDestroyBuildingsInCellIfInvalid(IntVec3 cell)
		{
			var terrain = cell.GetTerrain(map);
			var things = cell.GetThingList(map);
			for (int i = 0; i < things.Count; i++)
			{
				var thing = things[i];
				if (thing == null)
					continue; //Can't work on a null thing!
				bool dueToAffordances = false;
				bool shouldBreakdownOrDestroy = false;
				if (thing is Building building && thing.def.destroyable)
				{
					if ((thing.questTags != null && thing.questTags.Count > 0) || //If it's marked for a quest..
						(thing.def.defName.StartsWith("Ancient") || thing.def.defName.StartsWith("VFEA_")) || //If it's ancient stuff..
						BreakdownOrDestroyExceptedDefNames.Contains(thing.def.defName)) //Or if it's in the list of things to skip..
						continue; //Skip this one.
					if (thing.def.PlaceWorkers != null)
						foreach (PlaceWorker pw in thing.def.PlaceWorkers)
						{
							if (BreakdownOrDestroyExceptedPlaceWorkerTypeStrings.Contains(pw.ToString())) //If it's in the list to skip..
								continue; //Skip this one.
							var acceptanceReport = pw.AllowsPlacing(thing.def, thing.Position, thing.Rotation, map);
							if (!acceptanceReport) //Failed PlaceWorker
							{
								if (BreakdownOrDestroyExceptedPlaceWorkerFailureReasons.Contains(acceptanceReport.Reason)) //If it's a reason we don't care about.
									continue; //Don't destroy for this particular reason, irrelevant.
								Log.Message("PlaceWorker failed with reason: " + acceptanceReport.Reason);
								shouldBreakdownOrDestroy = true; 
								break; //We don't need to check more if we've found a reason to not be here.
							}
						}
					//Had no PlaceWorkers or it passed all their checks but it has an affordance that isn't being met.
					if (thing.TerrainAffordanceNeeded != null &&
						thing.TerrainAffordanceNeeded.defName != "" &&
						terrain.affordances != null &&
						!terrain.affordances.Contains(thing.TerrainAffordanceNeeded))
					{
						shouldBreakdownOrDestroy = true;
						dueToAffordances = true;
					}
					if (shouldBreakdownOrDestroy)
					{
						if (thing is ThingWithComps twc) //If it has comps..
						{
							var flickable = twc.GetComp<CompFlickable>();
							var breakdown = twc.GetComp<CompBreakdownable>();
							if (flickable != null && breakdown != null) //If it has both comps..
							{
								if (flickable.SwitchIsOn && !breakdown.BrokenDown) //If it's on and it isn't broken down..
								{
									breakdown.DoBreakdown(); //Cause breakdown.
									flickable.DoFlick(); //Turn it off.
								}
							}
							else if (!(dueToAffordances && terrain.IsWater) && breakdown != null) //It has breakdown but not flickable and this is not due to ice->water lacking affordance.
							{
								if (!breakdown.BrokenDown) //If it isn't already broken down..
									breakdown.DoBreakdown(); //Cause breakdown.
							}
							else //It has either only flickable or neither, or it's got only breakdown but it's due to water->ice lacking affordance.
								thing.Destroy(DestroyMode.FailConstruction);
						}
						else //No comps..
							thing.Destroy(DestroyMode.FailConstruction);
					}
				}
			}
        }

		public override void ExposeData()
		{
			List<float> iceDepthGridList = new List<float>();
			List<float> waterDepthGridList = new List<float>();
			List<float> pseudoElevationGridList = new List<float>();
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				List<string> naturalWaterTerrainGridStringList = new List<string>();
				List<string> allWaterTerrainGridStringList = new List<string>();
				if (AllWaterTerrainGrid != null)
					allWaterTerrainGridStringList = AllWaterTerrainGrid.Select(def => def == null ? "null" : def.defName).ToList();
				if (NaturalWaterTerrainGrid != null)
					naturalWaterTerrainGridStringList = NaturalWaterTerrainGrid.Select(def => def == null ? "null" : def.defName).ToList();
				if (IceDepthGrid != null)
					iceDepthGridList = IceDepthGrid.ToList();
				if (WaterDepthGrid != null)
					waterDepthGridList = WaterDepthGrid.ToList();
				if (PseudoWaterElevationGrid != null)
					pseudoElevationGridList = PseudoWaterElevationGrid.ToList();
				Scribe_Collections.Look(ref naturalWaterTerrainGridStringList, "NaturalWaterTerrainGrid");
				Scribe_Collections.Look(ref allWaterTerrainGridStringList, "AllWaterTerrainGrid");
            }
			Scribe_Collections.Look(ref iceDepthGridList, "IceDepthGrid");
			Scribe_Collections.Look(ref waterDepthGridList, "WaterDepthGrid");
			Scribe_Collections.Look(ref pseudoElevationGridList, "PseudoElevationGrid");
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				List<TerrainDef> naturalWaterTerrainGridList = new List<TerrainDef>();
				List<TerrainDef> allWaterTerrainGridList = new List<TerrainDef>();
				Scribe_Collections.Look(ref naturalWaterTerrainGridList, "NaturalWaterTerrainGrid");
				Scribe_Collections.Look(ref allWaterTerrainGridList, "AllWaterTerrainGrid");
				NaturalWaterTerrainGrid = naturalWaterTerrainGridList.ToArray();
				AllWaterTerrainGrid = allWaterTerrainGridList.ToArray();
				IceDepthGrid = iceDepthGridList.ToArray();
				WaterDepthGrid = waterDepthGridList.ToArray();
				PseudoWaterElevationGrid = pseudoElevationGridList.ToArray();
			}
			base.ExposeData();
        }
	}
}