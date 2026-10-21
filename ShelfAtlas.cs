using System.Linq;

public class ShelfAtlas {
	public record Frame(string SourceCast, uint RandomVariation, uint Layer);
	public record AtlasCoord(uint X, uint Y, uint Width, uint Height, uint Page);
	public readonly Dictionary<Frame, AtlasCoord> frames = [];

	public readonly uint pages;

	public ShelfAtlas(int destWidth, int destHeight, IEnumerable<TileData> tiles) {
		List<Shelf> shelves = [];
		uint currentY = 0;
		uint currentPage = 0;

		foreach (TileData tile in tiles.Where(t => t.Type == TileType.Standard || t.Type == TileType.RockType || t.Type == TileType.SandType)) {
			uint frameWidth = (uint) ((tile.Size.X + tile.BufferTiles * 2) * 20);
			uint frameHeight = (uint) ((tile.Size.Y + tile.BufferTiles * 2) * 20);

			if (frameWidth > destWidth || frameHeight > destHeight) {
				Console.WriteLine($"Error: Tile frames of {tile.Name} are larger than the destination texture.");
				continue;
			}

			for (uint rnd = 0; rnd < tile.RandomVariations; rnd++) {
				for (uint layer = 0; layer < tile.RepeatLayers.Length; layer++) {
					bool placed = false;
					foreach (Shelf shelf in shelves) {
						if (frameWidth > destWidth - shelf.X)
							continue;

						if (frameHeight > shelf.Height)
							continue;

						this.frames.Add(new Frame(tile.Name, rnd, layer), new AtlasCoord(shelf.X, shelf.Y, frameWidth, frameHeight, shelf.Page));
						shelf.X += frameWidth;
						placed = true;
						break;
					}

					if (placed)
						continue;

					if (currentY + frameHeight > destHeight) {
						currentPage++;
						currentY = 0;
					}

					Shelf newShelf = new Shelf() {
						X = 0,
						Y = currentY,
						Page = currentPage,
						Height = frameHeight,
					};

					this.frames.Add(new Frame(tile.Name, rnd, layer), new AtlasCoord(newShelf.X, newShelf.Y, frameWidth, frameHeight, newShelf.Page));
					newShelf.X += frameWidth;
					currentY += newShelf.Height;

					shelves.Add(newShelf);
				}
			}
		}

		this.pages = currentPage + 1;
	}

	public class Shelf {
		public uint Page;
		public uint X;
		public uint Y;
		public uint Height;
	}
}