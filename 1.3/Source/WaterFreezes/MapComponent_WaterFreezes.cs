﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using System.Runtime.CompilerServices;

namespace WF
{
    public class MapComponent_WaterFreezes : MapComponent
    {
		bool init;
        public TerrainDef[] NaturalWaterTerrainGrid;
		public TerrainDef[] AllWaterTerrainGrid;
		public float[] IceDepthGrid;
		public float[] WaterDepthGrid;
		public float[] PseudoWaterElevationGrid;
		int freezingMultiplier = 4;
		int thawingMultiplier = 2;
		float iceRate = 2500;
		//Ice thresholds of type by depth.
		float thresholdThinIce = 15;
		float thresholdIce = 50;
		float thresholdThickIce = 110;
		//Used for lakes *and* rivers.
		float maxWaterDeep = 400;
		float maxWaterShallow = 100;
		float maxIceDeep = 120;
		float maxIceShallow = 100;
		//Used for marshes.
		float maxWaterMarsh = 70;
		float maxIceMarsh = 70;

		public MapComponent_WaterFreezes(Map map) : base(map)
		{

		}

        public override void MapGenerated()
		{
			Initialize();
		}

		public void Initialize()
        {
			Log.Message("[WaterFreezes] Initializing..");
			if (WaterDepthGrid == null) //If we have no water depth grid..
			{
				Log.Message("[WaterFreezes] Instantiating water depth grid..");
				WaterDepthGrid = new float[map.cellIndices.NumGridCells]; //Instantiate it.
			}
			if (NaturalWaterTerrainGrid == null) //If we haven't got a waterGrid loaded from the save file, make one.
			{
				Log.Message("[WaterFreezes] Generating natural water grid and populating water depth grid..");
				NaturalWaterTerrainGrid = new TerrainDef[map.cellIndices.NumGridCells];
				for (int i = 0; i < map.cellIndices.NumGridCells; i++)
				{
					var c = map.cellIndices.IndexToCell(i);
					var t = c.GetTerrain(map);
					if (t == TerrainDefOf.WaterDeep)// || t == TerrainDefOf.WaterMovingChestDeep)
					{
						NaturalWaterTerrainGrid[i] = t;
						WaterDepthGrid[i] = maxWaterDeep;
					}
					else if (t == TerrainDefOf.WaterShallow)// || t == TerrainDefOf.WaterMovingShallow)
                    {
						NaturalWaterTerrainGrid[i] = t;
						WaterDepthGrid[i] = maxWaterShallow;
                    }
					else if (t == WaterDefs.Marsh)
                    {
						NaturalWaterTerrainGrid[i] = t;
						WaterDepthGrid[i] = maxWaterMarsh;
                    }
				}
			}
			if (AllWaterTerrainGrid == null) //If we have no all-water terrain grid..
			{
				Log.Message("[WaterFreezes] Cloning natural water grid into all water grid..");
				AllWaterTerrainGrid = (TerrainDef[])NaturalWaterTerrainGrid.Clone(); //Instantiate it to content of the natural water array for starters.
			}
			if (IceDepthGrid == null)
			{
				Log.Message("[WaterFreezes] Instantiating ice depth grid..");
				IceDepthGrid = new float[map.cellIndices.NumGridCells];
			}
			if (PseudoWaterElevationGrid == null)
			{
				Log.Message("[WaterFreezes] Generating pseudo water elevation grid..");
				PseudoWaterElevationGrid = new float[map.cellIndices.NumGridCells];
				UpdatePseudoWaterElevationGrid();
			}
			init = true;
		}

		public override void MapComponentTick()
		{
			if (!init) //If we aren't initialized..
				Initialize(); //Initialize it!
			if (Find.TickManager.TicksGame % iceRate != 0) //If it's not once per hour..
				return; //Don't execute the rest, throttling measure.
			for (int i = 0; i < NaturalWaterTerrainGrid.Length; i++) //Thread this later probably.
			{
				var cell = map.cellIndices.IndexToCell(i);
				var currentTerrain = cell.GetTerrain(map); //Get current terrain.
				//If it's lake ice or it's water, or it's a natural water spot..
				if (currentTerrain == IceDefs.WF_LakeIceThin ||
					currentTerrain == IceDefs.WF_LakeIce ||
					currentTerrain == IceDefs.WF_LakeIceThick ||
					currentTerrain == TerrainDefOf.WaterShallow ||
					currentTerrain == TerrainDefOf.WaterDeep ||
					//currentTerrain == TerrainDefOf.WaterMovingShallow ||
					//currentTerrain == TerrainDefOf.WaterMovingChestDeep ||
					currentTerrain == WaterDefs.Marsh ||
					AllWaterTerrainGrid[i] != null)
				{
					UpdateIceForTemperature(cell, currentTerrain);
					UpdateIceStage(cell, currentTerrain);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdatePseudoWaterElevationGrid()
        {
			Log.Message("[WaterFreezes] Updating pseudo water elevation grid..");
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
				if (AllWaterTerrainGrid[adjacentCellIndex] == null) //If it's land..
					pseudoElevationScore += 1; //+4 for each land
			}
			PseudoWaterElevationGrid[i] = pseudoElevationScore;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsTerrainDef(int i, Map map, TerrainDef def, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
		{
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			//Log.Message("[WaterFreezes] Checking if index " + i + " is " + def.defName + ".. currentTerrain: " + (currentTerrain == def) + ", underTerrain: " + (underTerrain == def) + ", AllWaterTerrainGrid: " + (AllWaterTerrainGrid[i] == def)); ;
			return currentTerrain == def || underTerrain == def || AllWaterTerrainGrid[i] == def;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsMarsh(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			return IsTerrainDef(i, map, WaterDefs.Marsh, currentTerrain, underTerrain);
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsShallowWater(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			return IsTerrainDef(i, map, TerrainDefOf.WaterShallow, currentTerrain, underTerrain);
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsDeepWater(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			return IsTerrainDef(i, map, TerrainDefOf.WaterDeep, currentTerrain, underTerrain);
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsLakeIce(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
		{
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			return IsTerrainDef(i, map, IceDefs.WF_LakeIceThin, currentTerrain, underTerrain) || 
				   IsTerrainDef(i, map, IceDefs.WF_LakeIce, currentTerrain, underTerrain) ||
				   IsTerrainDef(i, map, IceDefs.WF_LakeIceThick, currentTerrain, underTerrain);
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsMarshIce(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
		{
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			return IsTerrainDef(i, map, IceDefs.WF_MarshIceThin, currentTerrain, underTerrain) ||
				   IsTerrainDef(i, map, IceDefs.WF_MarshIce, currentTerrain, underTerrain);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsAnyWater(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			return IsShallowWater(i, map, currentTerrain, underTerrain) ||
				   IsDeepWater(i, map, currentTerrain, underTerrain) ||
				   IsMarsh(i, map, currentTerrain, underTerrain);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsLakeWater(int i, Map map, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
		{
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.TerrainAt(i); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			return IsShallowWater(i, map, currentTerrain, underTerrain) ||
				   IsDeepWater(i, map, currentTerrain, underTerrain);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateIceForTemperature(IntVec3 cell, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
		{
			var temperature = GenTemperature.GetTemperatureForCell(cell, map);
			if (temperature == 0) //If it's 0degC..
				return; //We don't update when ambiguous.
			int i = map.cellIndices.CellToIndex(cell);
			if (temperature < 0) //Temperature is below zero..
			{
				if (WaterDepthGrid[i] > 0)
				{
					if (PseudoWaterElevationGrid[i] == 0) //if this isn't one of the edge cells..
					{
						//If none of the adjacent cells have ice..
						if (!GenAdjFast.AdjacentCells8Way(cell).Any(adjacentCell =>
						{
							int adjacentCellIndex = map.cellIndices.CellToIndex(adjacentCell);
							if (adjacentCellIndex < 0 || adjacentCellIndex >= map.terrainGrid.topGrid.Length) //If it's a negative index or it's a larger index than the map's grid length (faster to get topGrid.Length than use property on the cellIndices).
								return false;
							var adjacentCellTerrain = map.terrainGrid.TerrainAt(adjacentCellIndex);
							return adjacentCellTerrain == IceDefs.WF_LakeIceThin ||
								   adjacentCellTerrain == IceDefs.WF_LakeIce ||
								   adjacentCellTerrain == IceDefs.WF_LakeIceThick ||
								   adjacentCellTerrain == IceDefs.WF_MarshIceThin ||
								   adjacentCellTerrain == IceDefs.WF_MarshIce;
						}))
							return; //We aren't going to freeze before there's ice adjacent to us.
					}
					var change = -temperature * (freezingMultiplier + PseudoWaterElevationGrid[i]) * // //Based on temperature but sped up by a multiplier which takes into account surrounding terrain.
						//(WaterDepthGrid[i] / 100) //* //Slow freezing by water depth per 100 water.
						(currentTerrain == TerrainDefOf.WaterDeep ? .5f : 1f) //* //If it's deep water right now, slow it down more.
						//(.9f + (.1f * Rand.Value)) //10% of the rate is variable for flavor.
						/ 2500 * iceRate; //Adjust to iceRate based on the 2500 we tuned it to originally.
					//Log.Message("[WaterFreezes] Freezing cell " + cell.ToString() + " for " + change + " amount, prior, ice was " + IceDepthGrid[i] + " and water was " + WaterDepthGrid[i]);
					IceDepthGrid[i] += change; //Ice goes up..
					if (IsShallowWater(i, map, currentTerrain, underTerrain))
						if (IceDepthGrid[i] > maxIceShallow)
							IceDepthGrid[i] = maxIceShallow;
					else if (IsDeepWater(i, map, currentTerrain, underTerrain))
						if (IceDepthGrid[i] > maxIceDeep)
							IceDepthGrid[i] = maxIceDeep;
					else if (IsMarsh(i, map, currentTerrain, underTerrain))
						if (IceDepthGrid[i] > maxIceMarsh)
							IceDepthGrid[i] = maxIceMarsh;
					WaterDepthGrid[i] -= change; //Water depth goes down..
					if (WaterDepthGrid[i] < 0)
						WaterDepthGrid[i] = 0;
					//Log.Message("[WaterFreezes] For cell " + cell.ToString() + " after changes (and clamping), ice was " + IceDepthGrid[i] + " and water was " + WaterDepthGrid[i]);
				}
			}
			else if (temperature > 0) //Temperature is above zero..
			{
				if (IceDepthGrid[i] > 0)
				{
					var change = temperature / (thawingMultiplier + PseudoWaterElevationGrid[i]) / // //Based on temperature but slowed down by a multiplier which takes into account surrounding terrain.
						(IceDepthGrid[i] / 100) * //Slow thawing further by ice thickness per 100 ice.
						(currentTerrain == IceDefs.WF_LakeIceThick ? .5f : 1f) *  //If it's thick ice right now, slow it down more.
						((currentTerrain == IceDefs.WF_LakeIce || currentTerrain == IceDefs.WF_MarshIce) ? .75f : 1f) //* //If it's regular ice right now, slow it down a little less than thick.
						//(.9f + (.1f * Rand.Value)) //10% of the rate is variable for flavor.
						/ 2500 * iceRate; //Adjust to iceRate based on the 2500 we tuned it to originally.
					IceDepthGrid[i] -= change; //Ice goes down..
					if (IceDepthGrid[i] < 0)
						IceDepthGrid[i] = 0;
					else //Only mess with water grid if ice grid had ice to melt.
					{
						WaterDepthGrid[i] += change; //Water depth goes up..
						if (IsShallowWater(i, map, currentTerrain, underTerrain) && WaterDepthGrid[i] > maxWaterShallow) //If shallow underneath and too much water,
							WaterDepthGrid[i] = maxWaterShallow; //Cap it.
						else if (IsDeepWater(i, map, currentTerrain, underTerrain) && WaterDepthGrid[i] > maxWaterDeep) //If deep underneath and too much water,
							WaterDepthGrid[i] = maxWaterDeep; //Cap it.
						else if (IsMarsh(i, map, currentTerrain, underTerrain) && WaterDepthGrid[i] > maxWaterMarsh) //If marsh underneath and too much water,
							WaterDepthGrid[i] = maxWaterMarsh; //Cap it.
					}
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateIceStage(IntVec3 cell, TerrainDef currentTerrain = null)
        {
			int i = map.cellIndices.CellToIndex(cell);
			float ice = IceDepthGrid[i];
			var water = AllWaterTerrainGrid[i];
			var isLake = water == TerrainDefOf.WaterDeep || water == TerrainDefOf.WaterShallow;
			var isMarsh = water == WaterDefs.Marsh;
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = map.terrainGrid.topGrid[i]; //Get it.
			if (ice < thresholdThinIce) //If there's no ice..
				ThawCell(cell);
			else
			{
				if (currentTerrain.bridge) //If it's a bridge
					return; //We're not updating the terrain, cuz it's under the bridge.
				if (ice < thresholdIce) //If there's ice, but it's below the regular ice depth threshold..
				{
					//If it's water then it's freezing now..
					if (currentTerrain == TerrainDefOf.WaterDeep ||
						currentTerrain == TerrainDefOf.WaterShallow ||
						currentTerrain == WaterDefs.Marsh)
						map.terrainGrid.SetUnderTerrain(cell, currentTerrain); //Store the water in under-terrain.
					if (isLake)
						map.terrainGrid.SetTerrain(cell, IceDefs.WF_LakeIceThin);
					else if (isMarsh)
						map.terrainGrid.SetTerrain(cell, IceDefs.WF_MarshIceThin);
				}
				else if (ice < thresholdThickIce) //If it's between regular ice and thick ice in depth..
				{
					if (isLake)
						map.terrainGrid.SetTerrain(cell, IceDefs.WF_LakeIce);
					else if (isMarsh)
						map.terrainGrid.SetTerrain(cell, IceDefs.WF_MarshIce);
				}
				else //Only thick left..
					map.terrainGrid.SetTerrain(cell, IceDefs.WF_LakeIceThick); //Only lakes have thick ice (right now), so no branching/checks here..
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ThawCell(IntVec3 cell, TerrainDef currentTerrain = null, TerrainDef underTerrain = null)
        {
			int i = map.cellIndices.CellToIndex(cell);
			if (currentTerrain == null) //If it wasn't passed in..
				currentTerrain = cell.GetTerrain(map); //Get it.
			if (underTerrain == null) //If it wasn't passed in..
				underTerrain = map.terrainGrid.UnderTerrainAt(i); //Get it.
			if (NaturalWaterTerrainGrid[i] != null) //If it's natural water..
			{
				if (!currentTerrain.bridge)
				{
					map.terrainGrid.SetTerrain(cell, NaturalWaterTerrainGrid[i]); //Make sure terrain is set to the right thing.
					Log.Message("Calling DestroyBuildingInCell from natural lake thaw code..");
					DestroyBuildingsInCell(cell);
				}
				var season = GenLocalDate.Season(map);
				if (season == Season.Spring || season == Season.Summer || season == Season.PermanentSummer) //If it's the right season..
				{
					//Refill the cell..
					if (NaturalWaterTerrainGrid[i] == TerrainDefOf.WaterDeep) //It's deep water when present..
					{
						if (WaterDepthGrid[i] < maxWaterDeep) //If it's not over-full..
							WaterDepthGrid[i] += 1 / 2500 * iceRate; //Fill
						if (WaterDepthGrid[i] > maxWaterDeep) //If it's too full..
							WaterDepthGrid[i] = maxWaterDeep; //Cap it.
					}
					else if (NaturalWaterTerrainGrid[i] == TerrainDefOf.WaterShallow) //It's shallow water when present..
					{
						if (WaterDepthGrid[i] < maxWaterShallow) //If it's not over-full..
							WaterDepthGrid[i] += 1 / 2500 * iceRate; //Fill
						if (WaterDepthGrid[i] > maxWaterShallow) //If it's too full..
							WaterDepthGrid[i] = maxWaterShallow; //Cap it.
					}
					else if (NaturalWaterTerrainGrid[i] == WaterDefs.Marsh) //It's marsh when present..
                    {
						if (WaterDepthGrid[i] < maxWaterMarsh) //If it's not over-full..
							WaterDepthGrid[i] += 1 / 2500 * iceRate; //Fill
						if (WaterDepthGrid[i] > maxWaterMarsh) //If it's too full..
							WaterDepthGrid[i] = maxWaterMarsh; //Cap it.
                    }
				}
			}
			else if (underTerrain != null && (underTerrain == TerrainDefOf.WaterShallow || underTerrain == TerrainDefOf.WaterDeep || underTerrain == WaterDefs.Marsh)) //If there was under-terrain and it's water.
			{
				if (WaterDepthGrid[i] > 0 && !currentTerrain.bridge) //If there's water there and it isn't under a bridge..
				{
					map.terrainGrid.SetTerrain(cell, underTerrain); //Set the top layer to the under-terrain
					map.terrainGrid.SetUnderTerrain(cell, null); //Clear the under-terrain
					Log.Message("Calling DestroyBuildingInCell from artificial lake thaw code..");
					DestroyBuildingsInCell(cell);
				}
			}
		}

		/// <summary>
		/// Removes ice from a cell, updates its ice stage, and reports the amount of ice removed (called from outside as part of the API).
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		public float TakeCellIce(IntVec3 cell)
		{
			int i = map.cellIndices.CellToIndex(cell);
			var ice = IceDepthGrid[i];
			IceDepthGrid[i] = 0;
			UpdateIceStage(cell);
			return ice;
        }

		/// <summary>
		/// Returns the amount of ice in a cell (called from outside as part of the API).
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		public float QueryCellIce(IntVec3 cell)
        {
			return IceDepthGrid[map.cellIndices.CellToIndex(cell)];
		}

		/// <summary>
		/// Returns the amount of water in a cell (called from outside as part of the API).
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		public float QueryCellWater(IntVec3 cell)
		{
			return WaterDepthGrid[map.cellIndices.CellToIndex(cell)];
		}

		/// <summary>
		/// Returns the natural water def of a cell or null if not present (called from outside as part of the API).
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		public TerrainDef QueryCellNaturalWater(IntVec3 cell)
        {
			return NaturalWaterTerrainGrid[map.cellIndices.CellToIndex(cell)];
        }

		/// <summary>
		/// Destroy all buildings incapable of existing on the cell and return whether any was skipped due to being impassable.
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		public void DestroyBuildingsInCell(IntVec3 cell)
		{
			if (cell == null)
				Log.Error("Cell null..?");
			if (map == null)
				Log.Error("Map null..?");
			var things = cell.GetThingList(map);
			if (things == null)
				Log.Error("things null.");
			foreach (var thing in things)
			{
				Log.Message("Assessing " + thing.def.defName + " for destruction on tile of type " + cell.GetTerrain(map).defName + " with " + thing.def.placeWorkers.Count + " placeworkers.");
				if (thing is Building && thing.def.destroyable)
					foreach (PlaceWorker pw in thing.def.PlaceWorkers)
						if (!pw.AllowsPlacing(thing.def, cell, thing.Rotation, map).Accepted)
							thing.Destroy(DestroyMode.Deconstruct);
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