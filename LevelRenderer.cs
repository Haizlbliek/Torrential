using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

public class LevelRenderer : IDisposable {
	public const uint CanvasWidth = 1400;
	public const uint CanvasHeight = 800;
	public const uint LayerCount = 30;

	private readonly GraphicsDevice gd;
	private readonly ResourceFactory factory;
	private readonly Shader[] shaders;
	private readonly ResourceLayout resourceLayout;
	private readonly Sampler sampler;
	private readonly Texture drawingCanvas;
	private readonly Framebuffer[] framebuffers = [];
	private readonly Pipeline pipeline;
	private readonly CommandList commandList;

	private LevelData? level;
	private List<TileData> tiles = [];
	private Dictionary<string, TileData> tilesByName = [];
	private GPUAtlas? atlas;
	private TextureView? arrayView;
	private ResourceSet? resourceSet;
	private DeviceBuffer? instanceBuffer;
	private uint instanceBufferCapacity;
	private Sdl2Window? hiddenWindow;

	public LevelRenderer() {
		this.gd = this.CreateGraphicsDevice();
		this.factory = this.gd.ResourceFactory;
		this.sampler = this.factory.CreateSampler(SamplerDescription.Point);

		ShaderDescription vertDesc = new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes("assets/shader.vert"), "main");
		ShaderDescription fragDesc = new ShaderDescription(ShaderStages.Fragment, File.ReadAllBytes("assets/shader.frag"), "main");

		this.shaders = this.factory.CreateFromSpirv(vertDesc, fragDesc);

		this.resourceLayout = this.factory.CreateResourceLayout(new ResourceLayoutDescription(
			new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
			new ResourceLayoutElementDescription("MainTextures", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
		));

		this.drawingCanvas = this.factory.CreateTexture(TextureDescription.Texture2D(
			CanvasWidth,
			CanvasHeight,
			1,
			LayerCount,
			PixelFormat.R8_G8_B8_A8_UNorm,
			TextureUsage.RenderTarget
		));

		this.framebuffers = new Framebuffer[LayerCount];

		for (uint layer = 0; layer < LayerCount; layer++) {
			this.framebuffers[layer] = this.factory.CreateFramebuffer(
				new FramebufferDescription(
					null,
					[
						new FramebufferAttachmentDescription(
							this.drawingCanvas,
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
			ResourceLayouts = [this.resourceLayout],
			ShaderSet = new ShaderSetDescription(
				vertexLayouts: [drawInstanceLayout],
				shaders: this.shaders
			),
			Outputs = this.framebuffers[0].OutputDescription
		};
		this.pipeline = this.factory.CreateGraphicsPipeline(pipelineDesc);

		this.commandList = this.factory.CreateCommandList();
	}

	public GraphicsDevice CreateGraphicsDevice() {
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

			this.hiddenWindow = VeldridStartup.CreateWindow(ref windowCI);

			return VeldridStartup.CreateGraphicsDevice(this.hiddenWindow, options, GraphicsBackend.OpenGL);
		}
		catch {
			this.hiddenWindow?.Close();
			this.hiddenWindow = null;
		}

		throw new Exception("Failed to initialize a compatible Graphics Device backend");
	}

	public void LoadLevel(string path) {
		this.LevelDispose();

		this.level = LevelData.Load(path);

		this.tiles = [.. Configs.ParseConfigFile("Graphics/Init.txt", this.level.UsedTiles)];
		this.tilesByName = this.tiles.ToDictionary(t => t.Name);
		this.tiles.Sort((a, b) => {
			int yCompare = (b.Size.Y + b.BufferTiles * 2).CompareTo(a.Size.Y + a.BufferTiles * 2);

			return yCompare != 0 ? yCompare : (b.Size.X + b.BufferTiles * 2).CompareTo(a.Size.X + a.BufferTiles * 2);
		});

		this.atlas = GPUAtlas.Build(this.gd, this.factory, this.tiles);

		this.arrayView = this.factory.CreateTextureView(this.atlas.Texture);

		this.resourceSet = this.factory.CreateResourceSet(new ResourceSetDescription(
			this.resourceLayout,
			this.sampler,
			this.arrayView
		));
	}

	public void Render() {
		if (this.level == null || this.atlas == null || this.resourceSet == null)
			throw new Exception("Called Render before LoadLevel");

		List<BlitInstance> drawOperations = [];

		for (uint x = 0; x < this.level.Width; x++) {
			for (uint y = 0; y < this.level.Height; y++) {
				for (uint z = 0; z < 3; z++) {
					LevelData.Material? mat = this.level.Materials[x, y][z];
					if (mat == null || !mat.Tile)
						continue;

					TileData tile = this.tilesByName[mat.CastName];

					uint sandOffset = tile.Type == TileType.SandType ? 1u : 0u;
					uint depth = z * 10 + sandOffset;
					bool randomPerLayer =
						tile.Type == TileType.RockType ||
						tile.Type == TileType.SandType;

					uint tileRandom = CustomRandom.Get(this.level.TileSeed, x, y, z);

					for (uint i = 0; i < tile.RepeatLayers.Length; i++) {
						for (uint j = i == 0 ? sandOffset : 0; j < tile.RepeatLayers[i]; j++) {
							if (depth >= LayerCount)
								break;

							uint random = randomPerLayer ? CustomRandom.Get(this.level.TileSeed, x, y, depth) : tileRandom;

							ShelfAtlas.Frame frame = new ShelfAtlas.Frame(mat.CastName, random % tile.RandomVariations, i);
							if (!this.atlas.Atlas.frames.TryGetValue(frame, out ShelfAtlas.AtlasCoord? coord)) {
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

						if (depth >= LayerCount)
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

		BlitInstance[] instances = [.. drawOperations];

		uint instanceBufferSize = (uint) (instances.Length * Marshal.SizeOf<BlitInstance>());

		if (this.instanceBuffer == null || instanceBufferSize > this.instanceBufferCapacity) {
			this.instanceBuffer?.Dispose();

			this.instanceBufferCapacity = Math.Max(
				instanceBufferSize,
				Math.Max(this.instanceBufferCapacity * 2, 4096u)
			);

			this.instanceBuffer = this.factory.CreateBuffer(
				new BufferDescription(
					this.instanceBufferCapacity,
					BufferUsage.VertexBuffer
				)
			);
		}

		if (instances.Length > 0)
			this.gd.UpdateBuffer(this.instanceBuffer, 0, instances);

		this.commandList.Begin();

		for (uint layer = 0; layer < LayerCount; layer++) {
			this.commandList.SetFramebuffer(this.framebuffers[layer]);

			this.commandList.ClearColorTarget(0, RgbaFloat.Clear);

			if (layerCounts[layer] == 0)
				continue;

			this.commandList.SetPipeline(this.pipeline);
			this.commandList.SetGraphicsResourceSet(0, this.resourceSet);
			this.commandList.SetVertexBuffer(0, this.instanceBuffer);

			this.commandList.Draw(
				vertexCount: 4,
				instanceCount: layerCounts[layer],
				vertexStart: 0,
				instanceStart: layerStarts[layer]
			);
		}

		this.commandList.End();

		this.gd.SubmitCommands(this.commandList);
		this.gd.WaitForIdle();

		Outputter.OutputCombined(this.gd, this.factory, this.drawingCanvas);
	}

	private void LevelDispose() {
		this.gd.WaitForIdle();

		this.resourceSet?.Dispose();
		this.resourceSet = null;

		this.arrayView?.Dispose();
		this.arrayView = null;

		this.atlas?.Dispose();
		this.atlas = null;

		this.level = null;
	}

	public void Dispose() {
		this.LevelDispose();

		this.commandList.Dispose();
		this.pipeline.Dispose();

		foreach (Framebuffer framebuffer in this.framebuffers)
			framebuffer.Dispose();

		this.drawingCanvas.Dispose();
		this.instanceBuffer?.Dispose();
		this.sampler.Dispose();
		this.resourceLayout.Dispose();

		foreach (Shader shader in this.shaders)
			shader.Dispose();

		this.gd.Dispose();
		this.hiddenWindow?.Close();
	}
}