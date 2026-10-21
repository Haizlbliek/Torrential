using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

public static partial class Program {
	public const uint CanvasWidth = 1400;
	public const uint CanvasHeight = 800;
	public const uint LayerCount = 30;

	public static GraphicsDevice CreateGraphicsDevice() {
		GraphicsDeviceOptions options = new GraphicsDeviceOptions() {
			Debug = false,
			HasMainSwapchain = false,
			ResourceBindingModel = ResourceBindingModel.Improved,
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
		};

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			try {
				return GraphicsDevice.CreateD3D11(options);
			}
			catch {
			}
		}

		try {
			return GraphicsDevice.CreateVulkan(options);
		}
		catch {
		}

		try {
			WindowCreateInfo windowCI = new WindowCreateInfo {
				X = 0,
				Y = 0,
				WindowWidth = 1,
				WindowHeight = 1,
				WindowInitialState = WindowState.Hidden,
				WindowTitle = ""
			};

			Sdl2Window hiddenWindow = VeldridStartup.CreateWindow(ref windowCI);

			return VeldridStartup.CreateGraphicsDevice(hiddenWindow, options, GraphicsBackend.OpenGL);
		}
		catch {
		}

		throw new Exception("Failed to initialize a compatible Graphics Device backend");
	}

	public static void Main() {
		Stopwatch total = Stopwatch.StartNew();
		Stopwatch gpuInit = Stopwatch.StartNew();
		using GraphicsDevice gd = CreateGraphicsDevice();
		ResourceFactory factory = gd.ResourceFactory;
		gpuInit.Stop();

		Stopwatch load = Stopwatch.StartNew();
		LevelData level = LevelData.Load("LevelEditorProjects/project.txt");

		List<TileData> tiles = [.. Configs.ParseConfigFile("Graphics/Init.txt", level.UsedTiles)];
		Dictionary<string, TileData> tilesByName = tiles.ToDictionary(t => t.Name);
		tiles.Sort((a, b) => {
			int yCompare = (b.Size.Y + b.BufferTiles * 2).CompareTo(a.Size.Y + a.BufferTiles * 2);

			return yCompare != 0 ? yCompare : (b.Size.X + b.BufferTiles * 2).CompareTo(a.Size.X + a.BufferTiles * 2);
		});
		load.Stop();

		Stopwatch atlasTimer = Stopwatch.StartNew();
		using GPUAtlas atlas = GPUAtlas.Build(gd, factory, tiles);
		atlasTimer.Stop();

		Stopwatch drawOps = Stopwatch.StartNew();
		List<BlitInstance> drawOperations = [];
		// TODO: Randomization
		for (uint x = 0; x < level.Width; x++) {
			for (uint y = 0; y < level.Height; y++) {
				for (uint z = 0; z < 3; z++) {
					LevelData.Material? mat = level.Materials[x, y][z];
					if (mat == null || !mat.Tile)
						continue;

					TileData tile = tilesByName[mat.CastName];

					uint sandOffset = tile.Type == TileType.SandType ? 1u : 0u;
					uint depth = z * 10 + sandOffset;
					bool randomPerLayer =
						tile.Type == TileType.RockType ||
						tile.Type == TileType.SandType;

					uint tileRandom = CustomRandom.Get(level.TileSeed, x, y, z);

					for (uint i = 0; i < tile.RepeatLayers.Length; i++) {
						for (uint j = i == 0 ? sandOffset : 0; j < tile.RepeatLayers[i]; j++) {
							if (depth >= Program.LayerCount)
								break;

							uint random = randomPerLayer ? CustomRandom.Get(level.TileSeed, x, y, depth) : tileRandom;

							ShelfAtlas.Frame frame = new ShelfAtlas.Frame(mat.CastName, random % tile.RandomVariations, i);
							if (!atlas.Atlas.frames.TryGetValue(frame, out ShelfAtlas.AtlasCoord? coord)) {
								depth++;
								continue;
							}

							drawOperations.Add(new BlitInstance(
								coord.X,
								coord.Y,
								coord.Width,
								coord.Height,
								coord.Page,
								(int) ((int) x * 20 - tile.BufferTiles * 20 - (tile.Size.X - 1) / 2 * 20),
								(int) ((int) y * 20 - tile.BufferTiles * 20 - (tile.Size.Y - 1) / 2 * 20),
								depth
							));
							depth++;
						}

						if (depth >= Program.LayerCount)
							break;
					}
				}
			}
		}
		drawOperations.Sort(
			(a, b) => a.DestLayer.CompareTo(b.DestLayer)
		);

		uint[] layerStarts = new uint[LayerCount];
		uint[] layerCounts = new uint[LayerCount];

		foreach (BlitInstance instance in drawOperations) {
			layerCounts[instance.DestLayer]++;
		}

		for (int i = 1; i < LayerCount; i++) {
			layerStarts[i] = layerStarts[i - 1] + layerCounts[i - 1];
		}
		drawOps.Stop();

		Stopwatch shaderSetup = Stopwatch.StartNew();
		ShaderDescription vertDesc = new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes("assets/shader.vert"), "main");
		ShaderDescription fragDesc = new ShaderDescription(ShaderStages.Fragment, File.ReadAllBytes("assets/shader.frag"), "main");

		Shader[] shaders = factory.CreateFromSpirv(vertDesc, fragDesc);
		shaderSetup.Stop();

		Stopwatch pipelineSetup = Stopwatch.StartNew();
		using ResourceLayout resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
			new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
			new ResourceLayoutElementDescription("MainTextures", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
		));


		using TextureView arrayView = factory.CreateTextureView(atlas.Texture);
		using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);

		using ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
			resourceLayout,
			sampler,
			arrayView
		));

		using Texture drawingCanvas = factory.CreateTexture(TextureDescription.Texture2D(
			CanvasWidth,
			CanvasHeight,
			1,
			LayerCount,
			PixelFormat.R8_G8_B8_A8_UNorm,
			TextureUsage.RenderTarget
		));

		Framebuffer[] framebuffers = new Framebuffer[LayerCount];

		for (uint layer = 0; layer < LayerCount; layer++) {
			framebuffers[layer] = factory.CreateFramebuffer(
				new FramebufferDescription(
					null,
					[
						new FramebufferAttachmentDescription(
							drawingCanvas,
							layer,
							0
						)
					]
				)
			);
		}

		VertexLayoutDescription drawInstanceLayout = new VertexLayoutDescription(
			new VertexElementDescription("inSrcX", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1),
			new VertexElementDescription("inSrcY", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1),
			new VertexElementDescription("inSrcWidth", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1),
			new VertexElementDescription("inSrcHeight", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1),
			new VertexElementDescription("inSrcPage", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1),
			new VertexElementDescription("inDestX", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int1),
			new VertexElementDescription("inDestY", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int1),
			new VertexElementDescription("inDestLayer", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt1)
		) {
			InstanceStepRate = 1
		};

		GraphicsPipelineDescription pipelineDesc = new GraphicsPipelineDescription {
			BlendState = BlendStateDescription.SingleOverrideBlend,
			DepthStencilState = DepthStencilStateDescription.Disabled,
			RasterizerState = RasterizerStateDescription.CullNone,
			PrimitiveTopology = PrimitiveTopology.TriangleStrip,
			ResourceLayouts = [resourceLayout],
			ShaderSet = new ShaderSetDescription(
				vertexLayouts: [drawInstanceLayout],
				shaders: shaders
			),
			Outputs = framebuffers[0].OutputDescription
		};
		using Pipeline pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
		pipelineSetup.Stop();

		Stopwatch blitSetup = Stopwatch.StartNew();
		BlitInstance[] instances = [.. drawOperations];

		uint instanceBufferSize = (uint) (instances.Length * Marshal.SizeOf<BlitInstance>());

		using DeviceBuffer instanceBuffer = factory.CreateBuffer(
			new BufferDescription(
				Math.Max(instanceBufferSize, 4u),
				BufferUsage.VertexBuffer
			)
		);

		if (instances.Length > 0)
			gd.UpdateBuffer(instanceBuffer, 0, instances);

		blitSetup.Stop();

		Stopwatch commands = Stopwatch.StartNew();
		using CommandList cl = factory.CreateCommandList();

		cl.Begin();

		for (uint layer = 0; layer < LayerCount; layer++) {
			cl.SetFramebuffer(framebuffers[layer]);

			cl.ClearColorTarget(0, RgbaFloat.Clear);

			if (layerCounts[layer] == 0)
				continue;

			cl.SetPipeline(pipeline);
			cl.SetGraphicsResourceSet(0, resourceSet);
			cl.SetVertexBuffer(0, instanceBuffer);

			cl.Draw(
				vertexCount: 4,
				instanceCount: layerCounts[layer],
				vertexStart: 0,
				instanceStart: layerStarts[layer]
			);
		}

		cl.End();

		gd.SubmitCommands(cl);
		gd.WaitForIdle();
		commands.Stop();
		total.Stop();

		Outputter.OutputCombined(gd, factory, drawingCanvas);

		foreach (Framebuffer framebuffer in framebuffers)
			framebuffer.Dispose();
		foreach (Shader shader in shaders)
			shader.Dispose();

		Console.WriteLine($"GPUInit: {gpuInit.ElapsedMilliseconds} ms");
		Console.WriteLine($"Load: {load.ElapsedMilliseconds} ms");
		Console.WriteLine($"Atlas: {atlasTimer.ElapsedMilliseconds} ms");
		Console.WriteLine($"DrawOps: {drawOps.ElapsedMilliseconds} ms");
		Console.WriteLine($"Shaders: {shaderSetup.ElapsedMilliseconds} ms");
		Console.WriteLine($"Pipeline: {pipelineSetup.ElapsedMilliseconds} ms");
		Console.WriteLine($"BlitSetup: {blitSetup.ElapsedMilliseconds} ms");
		Console.WriteLine($"Commands: {commands.ElapsedMilliseconds} ms");
		Console.WriteLine($"Total: {total.ElapsedMilliseconds} ms");
	}
}