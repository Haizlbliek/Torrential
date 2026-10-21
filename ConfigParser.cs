using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static partial class Configs {
	[GeneratedRegex(@"^\[(?!!-)(.*)\]$")]
	private static partial Regex TileLineRegex();

	[GeneratedRegex(@"#(?<key>\w+)\s*:\s*(?:""(?<string>[^""]+)""|point\((?<px>\d+)\s*,\s*(?<py>\d+)\)|\[(?<array>[^\]]*)\]|(?<number>-?\d+))")]
	private static partial Regex PropertyRegex();

	public static readonly Dictionary<string, string> castLookup = [];

	private static TileType MapStringToTileType(string raw) {
		return raw switch {
			"voxelStruct" => TileType.Standard,
			"voxelStructRockType" => TileType.RockType,
			"voxelStructRandomDisplaceHorizontal" => TileType.RandomDisplaceHorizontal,
			"voxelStructRandomDisplaceVertical" => TileType.RandomDisplaceVertical,
			"voxelStructSandType" => TileType.SandType,
			"voxelStructSeamlessHorizontal" => TileType.SeamlessHorizontal,
			"voxelStructSeamlessVertical" => TileType.SeamlessVertical,
			"box" => TileType.Box,
			_ => throw new ArgumentException($"Unknown tile type: {raw}")
		};
	}

	private static void LoadCast() {
		string castPath = "drizzle-cast/";
		foreach (string filePath in Directory.EnumerateFiles(castPath)) {
			string id = Path.GetFileNameWithoutExtension(filePath);
			id = id[(id.IndexOf('_') + 1)..];
			id = id[(id.IndexOf('_') + 1)..];
			castLookup[id.ToLowerInvariant()] = filePath;
		}

		string graphicsPath = "Graphics/";
		foreach (string filePath in Directory.EnumerateFiles(graphicsPath)) {
			string id = Path.GetFileNameWithoutExtension(filePath);
			castLookup[id.ToLowerInvariant()] = filePath;
		}
	}

	public static IEnumerable<TileData> ParseConfigFile(string filePath, HashSet<string> UsedTiles) {
		LoadCast();

		using StreamReader reader = new StreamReader(filePath);

		HashSet<string> added = [];

		string? line;
		while ((line = reader.ReadLine()) != null) {
			line = line.Trim();

			if (string.IsNullOrEmpty(line) || line.StartsWith('-'))
				continue;

			Match tileMatch = TileLineRegex().Match(line);
			if (!tileMatch.Success) {
				Console.WriteLine($"Failed to parse line: {line}");
				continue;
			}

			string innerContent = tileMatch.Groups[1].Value;
			TileData tile = new TileData();

			foreach (Match propMatch in PropertyRegex().Matches(innerContent)) {
				string key = propMatch.Groups["key"].Value;

				if (propMatch.Groups["string"].Success) {
					string stringValue = propMatch.Groups["string"].Value;

					if (key == "nm")
						tile.Name = stringValue.ToLowerInvariant();

					else if (key == "tp")
						tile.Type = MapStringToTileType(stringValue);
				}
				else if (propMatch.Groups["number"].Success) {
					string numberValue = propMatch.Groups["number"].Value;

					if (key == "bfTiles")
						tile.BufferTiles = uint.Parse(numberValue);

					else if (key == "rnd")
						tile.RandomVariations = uint.Parse(numberValue);

					else if (key == "specs2")
						tile.Collision2Specification = [];
				}
				else if (propMatch.Groups["px"].Success) {
					int x = int.Parse(propMatch.Groups["px"].Value);
					int y = int.Parse(propMatch.Groups["py"].Value);

					if (key == "sz")
						tile.Size = new Vector2i(x, y);
				}
				else if (propMatch.Groups["array"].Success) {
					string raw = propMatch.Groups["array"].Value;
					if (key == "specs")
						tile.CollisionSpecification = [.. raw.Split(',', StringSplitOptions.TrimEntries).Select(int.Parse)];

					else if (key == "specs2")
						tile.Collision2Specification = [.. raw.Split(',', StringSplitOptions.TrimEntries).Select(int.Parse)];

					else if (key == "repeatL")
						tile.RepeatLayers = [.. raw.Split(',', StringSplitOptions.TrimEntries).Select(int.Parse)];

					else if (key == "tags")
						tile.Tags = [.. raw.Split(',', StringSplitOptions.TrimEntries)];
				}
				else {
					throw new ArgumentException($"Failed to parse property: {propMatch.Value}");
				}
			}

			if (!UsedTiles.Contains(tile.Name))
				continue;

			if (added.Contains(tile.Name))
				continue;

			if (tile.Type == TileType.RockType || tile.Type == TileType.SandType) {
				tile.RepeatLayers = [ tile.Collision2Specification.Length == 0 ? 10 : 20 ];
			}

			added.Add(tile.Name);

			yield return tile;
		}
	}
}