using System.IO;
using System.Runtime.InteropServices;
using StbImageWriteSharp;
using Veldrid;

public class Outputter {
	public static void Output(GraphicsDevice gd, ResourceFactory factory, Texture drawingCanvas) {
		TextureDescription stagingDesc = TextureDescription.Texture2D(
			width: Program.CanvasWidth,
			height: Program.CanvasHeight,
			mipLevels: 1,
			arrayLayers: Program.LayerCount,
			format: PixelFormat.R8_G8_B8_A8_UNorm,
			usage: TextureUsage.Staging
		);
		using Texture stagingTexture = factory.CreateTexture(stagingDesc);

		using CommandList copyCl = factory.CreateCommandList();

		copyCl.Begin();

		copyCl.CopyTexture(
			source: drawingCanvas,
			srcX: 0,
			srcY: 0,
			srcZ: 0,
			srcMipLevel: 0,
			srcBaseArrayLayer: 0,

			destination: stagingTexture,
			dstX: 0,
			dstY: 0,
			dstZ: 0,
			dstMipLevel: 0,
			dstBaseArrayLayer: 0,

			width: Program.CanvasWidth,
			height: Program.CanvasHeight,
			depth: 1,
			layerCount: Program.LayerCount
		);

		copyCl.End();

		gd.SubmitCommands(copyCl);
		gd.WaitForIdle();

		ImageWriter pngWriter = new ImageWriter();
		Directory.CreateDirectory("Outputs");

		for (uint layer = 0; layer < Program.LayerCount; layer++) {
			MappedResource mappedView = gd.Map(stagingTexture, MapMode.Read, subresource: layer);

			int rowPitch = (int) mappedView.RowPitch;
			byte[] localBuffer = new byte[Program.CanvasWidth * Program.CanvasHeight * 4];

			unsafe {
				byte* srcPtr = (byte*) mappedView.Data;

				for (int row = 0; row < Program.CanvasHeight; row++) {
					Marshal.Copy(
						source: (IntPtr) (srcPtr + (row * rowPitch)),
						destination: localBuffer,
						startIndex: (int) (row * Program.CanvasWidth * 4),
						length: (int) (Program.CanvasWidth * 4)
					);
				}
			}

			gd.Unmap(stagingTexture, subresource: layer);

			using FileStream fileStream = File.Create($"Outputs/layer_{layer}.png");
			pngWriter.WritePng(localBuffer, (int) Program.CanvasWidth, (int) Program.CanvasHeight, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fileStream);
		}
	}

	public static void OutputCombined(GraphicsDevice gd, ResourceFactory factory, Texture drawingCanvas) {
		uint width = Program.CanvasWidth;
		uint height = Program.CanvasHeight;
		uint layers = Program.LayerCount;

		using Texture stagingTexture = factory.CreateTexture(
			TextureDescription.Texture2D(
				width,
				height,
				1,
				layers,
				PixelFormat.R8_G8_B8_A8_UNorm,
				TextureUsage.Staging
			)
		);

		using CommandList copyCl = factory.CreateCommandList();

		copyCl.Begin();
		copyCl.CopyTexture(
			drawingCanvas, 0, 0, 0, 0, 0,
			stagingTexture, 0, 0, 0, 0, 0,
			width, height, 1, layers
		);
		copyCl.End();

		gd.SubmitCommands(copyCl);
		gd.WaitForIdle();

		int outputWidth = (int) (width + layers - 1);
		int outputHeight = (int) (height + layers - 1);

		byte[] combined = new byte[outputWidth * outputHeight * 4];
		Array.Fill(combined, (byte)255);

		for (int layer = (int) layers - 1; layer >= 0; layer--) {
			MappedResource mapped = gd.Map(stagingTexture, MapMode.Read, (uint) layer);
			float whiteness = layer / (float) layers * 0.5f;

			try {
				unsafe {
					byte* src = (byte*) mapped.Data;

					for (int y = 0; y < height; y++) {
						for (int x = 0; x < width; x++) {
							byte* pixel = src + y * mapped.RowPitch + x * 4;

							byte alpha = pixel[3];

							if (alpha == 0)
								continue;

							int destX = x + (int) layers - 1 - layer;
							int destY = y + (int) layers - 1 - layer;

							int destIndex = (destY * outputWidth + destX) * 4;

							combined[destIndex] = (byte) (pixel[0] + (255 - pixel[0]) * whiteness);
							combined[destIndex + 1] = (byte) (pixel[1] + (255 - pixel[1]) * whiteness);
							combined[destIndex + 2] = (byte) (pixel[2] + (255 - pixel[2]) * whiteness);
							combined[destIndex + 3] = alpha;
						}
					}
				}
			}
			finally {
				gd.Unmap(stagingTexture, (uint) layer);
			}
		}

		Directory.CreateDirectory("Outputs");

		using FileStream stream = File.Create("Outputs/combined.png");

		new ImageWriter().WritePng(
			combined,
			outputWidth,
			outputHeight,
			ColorComponents.RedGreenBlueAlpha,
			stream
		);
	}
}