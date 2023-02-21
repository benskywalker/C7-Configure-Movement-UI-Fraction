using System.Linq;

namespace C7GameData
/*
	The save format is intended to be serialized to JSON upon saving
	and deserialized from JSON upon loading.

	The names are capitalized per C#, but I intend to use JsonSerializer
	settings to use camel case instead, unless there is reason not to.
*/
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.IO.Compression;
	using System.Text.Json;

	public enum SaveCompression {
		None,
		Zip,
		Invalid,
	}

	public class C7SaveFormat {
		public string Version = "v0.0early-prototype";

		// This naming is probably bad form, but it makes sense to me to name it as such here
		public GameData GameData;

		public C7SaveFormat() {
			GameData = new GameData();
		}

		public C7SaveFormat(GameData gameData) {
			GameData = gameData;
		}

		// postLoadActions inflates things in the save that are stored by reference,
		// and recomputes data such as tile neighbors not stored in the save.
		private void postLoadActions() {
			// Inflate tiles
			foreach (Tile tile in GameData.map.tiles) {
				if (tile.ResourceKey == "NONE") {
					tile.Resource = Resource.NONE;
				} else {
					tile.Resource = GameData.Resources.Find(r => r.Key == tile.ResourceKey);
				}
				tile.baseTerrainType = GameData.terrainTypes.Find(t => t.Key == tile.baseTerrainTypeKey);
				tile.overlayTerrainType = GameData.terrainTypes.Find(t => t.Key == tile.overlayTerrainTypeKey);
			}

			// Inflate experience levels
			var levelsByKey = new Dictionary<string, ExperienceLevel>();
			foreach (ExperienceLevel eL in GameData.experienceLevels)
				levelsByKey.Add(eL.key, eL);
			GameData.defaultExperienceLevel = levelsByKey[GameData.defaultExperienceLevelKey];
			foreach (MapUnit unit in GameData.mapUnits)
				unit.experienceLevel = levelsByKey[unit.experienceLevelKey];

			// Inflate barbarian info
			List<UnitPrototype> prototypes = GameData.unitPrototypes.Values.ToList();
			GameData.barbarianInfo.basicBarbarian = prototypes[GameData.barbarianInfo.basicBarbarianIndex];
			GameData.barbarianInfo.advancedBarbarian = prototypes[GameData.barbarianInfo.advancedBarbarianIndex];
			GameData.barbarianInfo.barbarianSeaUnit = prototypes[GameData.barbarianInfo.barbarianSeaUnitIndex];

			// Inflate tile neighbors
			GameData.map.computeNeighbors();
		}

		static SaveCompression getCompression(string path) {
			var ext = Path.GetExtension(path);
			if (ext.Equals(".JSON", StringComparison.CurrentCultureIgnoreCase)) {
				return SaveCompression.None;
			} else if (ext.Equals(".ZIP", StringComparison.CurrentCultureIgnoreCase)) {
				return SaveCompression.Zip;
			}
			return SaveCompression.Invalid;
		}

		public static C7SaveFormat Load(string path) {
			SaveCompression format = getCompression(path);
			C7SaveFormat save = null;
			if (format == SaveCompression.None) {
				save = JsonSerializer.Deserialize<C7SaveFormat>(File.ReadAllText(path), JsonOptions);
			} else {
				using (var archive = new ZipArchive(new FileStream(path, FileMode.Open), ZipArchiveMode.Read)) {
					ZipArchiveEntry entry = archive.GetEntry("save");
					using (Stream stream = entry.Open()) {
						save = JsonSerializer.Deserialize<C7SaveFormat>(stream, JsonOptions);
					}
				}
			}
			save.postLoadActions();
			return save;
		}

		public static void Save(C7SaveFormat save, string path) {
			SaveCompression format = getCompression(path);
			byte[] json = JsonSerializer.SerializeToUtf8Bytes(save, JsonOptions);
			if (format == SaveCompression.Zip) {
				using (var zipStream = new MemoryStream()) {
					var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
					ZipArchiveEntry entry = archive.CreateEntry("save");
					using (Stream stream = entry.Open()) {
						stream.Write(json, 0, json.Length);
					}
					// ZipArchive needs to be disposed in order for its content
					// to be written to the MemoryStream
					// https://stackoverflow.com/questions/12347775/ziparchive-creates-invalid-zip-file#12350106
					archive.Dispose();
					File.WriteAllBytes(path, zipStream.ToArray());
				}
			} else {
				File.WriteAllBytes(path, json);
			}
		}

		public static JsonSerializerOptions JsonOptions {
			get => new JsonSerializerOptions {
				// Lower-case the first letter in JSON because JSON naming standards
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				// Pretty print during development; may change this for production
				WriteIndented = true,
				// By default it only serializes getters, this makes it serialize fields, too
				IncludeFields = true,

				Converters = {
					// Serialize 2D array types
					new Json2DArrayConverter()
				},
			};
		}

	}
}
