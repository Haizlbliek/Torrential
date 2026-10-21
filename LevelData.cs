using System.IO;

public partial class LevelData {
	public record GeometryTile(int Type, int[] Flags);
	public record Material(bool Tile, string CastName);

	public int Width;
	public int Height;
	public uint TileSeed;
	public bool Lighting;
	public required GeometryTile[,][] Geometry;
	public required Material?[,][] Materials;
	public required HashSet<string> UsedTiles;


	private static int[] ParseFlags(ReadOnlySpan<char> token) {
		if (token.IsEmpty || token.IsWhiteSpace())
			return [];

		int count = 1;
		foreach (char c in token)
			if (c == ',')
				count++;

		int[] flags = new int[count];
		int idx = 0;

		MemoryExtensions.SpanSplitEnumerator<char> tokenizer = token.Split(',');
		foreach (Range range in tokenizer) {
			ReadOnlySpan<char> flagToken = token[range].Trim();
			if (flagToken.IsEmpty) continue;
			
			if (int.TryParse(flagToken, out int val)) {
				flags[idx++] = val;
			}
		}

		if (idx < count) {
			Array.Resize(ref flags, idx);
		}

		return flags;
	}

	public static LevelData Load(string path) {
		using StreamReader reader = new StreamReader(path);
		string geometryLine = reader.ReadLine()!;
		string materialsLine = reader.ReadLine()!;

		for (int i = 0; i < 3; i++) {
			reader.ReadLine();
		}

		string metadataLine = reader.ReadLine()!;

		ReadOnlySpan<char> metadataSpan = metadataLine.AsSpan();
		int seedIdx = metadataSpan.IndexOf("#tileSeed: ");
		int seedStart = seedIdx + 11;
		int seedEnd = metadataSpan[seedStart..].IndexOf(',');
		uint tileSeed = uint.Parse(metadataSpan.Slice(seedStart, seedEnd));

		int sizeIdx = metadataSpan.IndexOf("#size: point(");
		int sizeStart = sizeIdx + 13;
		int commaIdx = metadataSpan[sizeStart..].IndexOf(',');
		int width = int.Parse(metadataSpan.Slice(sizeStart, commaIdx));

		int sizeYStart = sizeStart + commaIdx + 2;
		int closeParenIdx = metadataSpan[sizeYStart..].IndexOf(')');
		int height = int.Parse(metadataSpan.Slice(sizeYStart, closeParenIdx));

		int lightIdx = metadataSpan.IndexOf("#light: ");
		bool lighting = metadataSpan[lightIdx + 8] == '1';

		GeometryTile[,][] tiles = new GeometryTile[width, height][];
		ReadOnlySpan<char> geometrySpan = geometryLine.AsSpan();

		int currentIdx = 0;

		while (geometrySpan[currentIdx] != '[')
			currentIdx++;
		currentIdx++;

		for (int x = 0; x < width; x++) {
			while (geometrySpan[currentIdx] != '[')
				currentIdx++;
			currentIdx++;

			for (int y = 0; y < height; y++) {
				while (geometrySpan[currentIdx] != '[')
					currentIdx++;
				currentIdx++;

				tiles[x, y] = new GeometryTile[3];

				for (int z = 0; z < 3; z++) {
					while (geometrySpan[currentIdx] != '[')
						currentIdx++;
					currentIdx++;

					int typeStart = currentIdx;
					while (char.IsDigit(geometrySpan[currentIdx]) || geometrySpan[currentIdx] == '-')
						currentIdx++;
					int type = int.Parse(geometrySpan[typeStart..currentIdx]);

					while (geometrySpan[currentIdx] != '[')
						currentIdx++;
					currentIdx++;

					int flagsStart = currentIdx;
					while (geometrySpan[currentIdx] != ']')
						currentIdx++;
					int flagsEnd = currentIdx;
					currentIdx++;

					int[] flags = ParseFlags(geometrySpan[flagsStart..flagsEnd]);

					tiles[x, y][z] = new GeometryTile(type, flags);
				}

				while (geometrySpan[currentIdx] != ']')
					currentIdx++;
				currentIdx++;
				currentIdx++;
			}

			while (geometrySpan[currentIdx] != ']')
				currentIdx++;
			currentIdx++;
		}

		Material?[,][] materials = new Material?[width, height][];
		HashSet<string> UsedTiles = [];
		ReadOnlySpan<char> materialsSpan = materialsLine.AsSpan();
		int defaultMaterialStart = materialsSpan.IndexOf("#defaultMaterial: ") + 19;
		int defaultMaterialEnd = defaultMaterialStart;
		while (materialsSpan[defaultMaterialEnd] != '\"') {
			defaultMaterialEnd++;
		}
		string defaultMaterial = materialsSpan[defaultMaterialStart..defaultMaterialEnd].ToString();
		materialsSpan = materialsSpan[(materialsSpan.IndexOf("[[[[") + 1)..];
		materialsSpan = materialsSpan[..(materialsSpan.LastIndexOf("]]]]") + 3)];

		currentIdx = 0;

		for (int x = 0; x < width; x++) {
			while (materialsSpan[currentIdx] != '[')
				currentIdx++;
			currentIdx++;

			for (int y = 0; y < height; y++) {
				while (materialsSpan[currentIdx] != '[')
					currentIdx++;
				currentIdx++;

				materials[x, y] = new Material[3];

				for (int z = 0; z < 3; z++) {
					while (materialsSpan[currentIdx] != '[')
						currentIdx++;
					currentIdx += 7; // `[#tp: "`

					int typeStart = currentIdx;
					while (materialsSpan[currentIdx] != '"')
						currentIdx++;
					int typeEnd = currentIdx;

					currentIdx += 10; // `", #data: `
					if (materialsSpan[typeStart] == 'd') { // default
						currentIdx++; // `0`
						materials[x, y][z] = new Material(false, defaultMaterial);
					}
					else if (materialsSpan[typeStart] == 'm') { // material
						currentIdx++;
						string material = "";
						while (materialsSpan[currentIdx] != '"') {
							material += materialsSpan[currentIdx];
							currentIdx++;
						}
						materials[x, y][z] = new Material(false, material);
					}
					else if (materialsSpan[typeStart + 4] == 'H') { // tileHead
						while (materialsSpan[currentIdx] != '"')
							currentIdx++;
						currentIdx++;
						string material = "";
						while (materialsSpan[currentIdx] != '"') {
							material += materialsSpan[currentIdx];
							currentIdx++;
						}
						currentIdx += 2;
						material = material.ToLowerInvariant();
						materials[x, y][z] = new Material(true, material);
						UsedTiles.Add(material);
					}
					else { // tileBody
						while (materialsSpan[currentIdx] != ']')
							currentIdx++;
						currentIdx++;
						materials[x, y][z] = null;
					}
				}

				while (materialsSpan[currentIdx] != ']')
					currentIdx++;
				currentIdx++;
				currentIdx++;
			}

			while (materialsSpan[currentIdx] != ']')
				currentIdx++;
			currentIdx++;
		}

		LevelData data = new LevelData() {
			Width = width,
			Height = height,
			TileSeed = tileSeed,
			Lighting = lighting,
			Geometry = tiles,
			Materials = materials,
			UsedTiles = UsedTiles,
		};

		return data;
	}
}