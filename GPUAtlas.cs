using System.IO;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Veldrid;

public class GPUAtlas : IDisposable {
	public const int destWidth = 8_192;
	public const int destHeight = 8_192;

	public ShelfAtlas Atlas { get; }
	public Texture Texture { get; }

	public GPUAtlas(ShelfAtlas atlas, Texture texture) {
		this.Atlas = atlas;
		this.Texture = texture;
	}

	private static readonly Dictionary<string, ImageResult> imageCache = [];

	private static ImageResult GetSourceImage(string castName) {
		if (imageCache.TryGetValue(castName, out ImageResult? image))
			return image;

		using FileStream stream =
			File.OpenRead(Configs.castLookup[castName]);

		image = ImageResult.FromStream(
			stream,
			StbImageSharp.ColorComponents.RedGreenBlueAlpha
		);

		imageCache.Add(castName, image);

		return image;
	}

	public static GPUAtlas Build(GraphicsDevice gd, ResourceFactory factory, List<TileData> tiles) {
		ShelfAtlas atlas = new ShelfAtlas(destWidth, destHeight, tiles);

		int components = 4;
		byte[] destData = new byte[destWidth * destHeight * components];
		TextureDescription desc = TextureDescription.Texture2D(
			width: destWidth,
			height: destHeight,
			mipLevels: 1,
			arrayLayers: atlas.pages,
			format: PixelFormat.R8_G8_B8_A8_UNorm,
			usage: TextureUsage.Sampled
		);
		Texture gpuAtlasArray = factory.CreateTexture(desc);

		for (int i = 0; i < atlas.pages; i++) {
			// Array.Clear(destData, 0, destData.Length);
			// Array.Fill(destData, (byte)255);

			foreach (KeyValuePair<ShelfAtlas.Frame, ShelfAtlas.AtlasCoord> kvp in atlas.frames) {
				if (kvp.Value.Page != i)
					continue;

				ImageResult source = GetSourceImage(kvp.Key.SourceCast);

				CopyRectangle(
					source.Data, source.Width, source.Height,
					kvp.Value.Width * kvp.Key.RandomVariation, 1 + kvp.Value.Height * kvp.Key.Layer,
					kvp.Value.Width, kvp.Value.Height,
					destData, destWidth,
					kvp.Value.X, kvp.Value.Y
				);
			}

			// using (FileStream stream = File.OpenWrite($"output{i}.png")) {
			// 	ImageWriter writer = new ImageWriter();
			// 	writer.WritePng(destData, destWidth, destHeight, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
			// }

			gd.UpdateTexture(
				texture: gpuAtlasArray,
				source: destData,
				x: 0, y: 0, z: 0,
				width: destWidth,
				height: destHeight,
				depth: 1,
				mipLevel: 0,
				arrayLayer: (uint) i
			);
		}

		return new GPUAtlas(atlas, gpuAtlasArray);
	}

	static unsafe void CopyRectangle(byte[] srcData, int srcWidth, int srcHeight, uint srcX, uint srcY, uint rectWidth, uint rectHeight, byte[] destData, int destWidth, uint destX, uint destY, int components = 4) {
		uint copyWidth = srcX < (uint)srcWidth
			? Math.Min(rectWidth, (uint)srcWidth - srcX)
			: 0;

		uint copyHeight = srcY < (uint)srcHeight
			? Math.Min(rectHeight, (uint)srcHeight - srcY)
			: 0;

		fixed (byte* pSrc = srcData)
		fixed (byte* pDest = destData) {
			for (uint y = 0; y < rectHeight; y++) {
				uint destOffset = ((destY + y) * (uint)destWidth + destX) * (uint)components;
				byte* destRow = pDest + destOffset;

				Unsafe.InitBlock(destRow, 255, rectWidth * (uint)components);

				if (y >= copyHeight || copyWidth == 0)
					continue;

				uint srcOffset = ((srcY + y) * (uint)srcWidth + srcX) * (uint)components;

				Unsafe.CopyBlock(
					destRow,
					pSrc + srcOffset,
					copyWidth * (uint)components
				);
			}
		}
	}

	public void Dispose() {
		this.Texture.Dispose();
	}
}