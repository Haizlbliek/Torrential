public struct TileData {
	public string Name = "";
	public TileType Type;
	public Vector2i Size;
	public int[] CollisionSpecification = [];
	public int[] Collision2Specification = [];
	public uint BufferTiles; // Might technically be a float
	public uint RandomVariations = 1; // Might technically be a float
	public int[] RepeatLayers = [];
	public string[] Tags = [];

	public TileData() {
	}
}

public enum TileType {
	Standard,
	RockType,
	RandomDisplaceHorizontal,
	RandomDisplaceVertical,
	SeamlessHorizontal,
	SeamlessVertical,
	SandType, // Community Editor only
	Box, // Deprecated
}