using System.Diagnostics;

public static partial class Program {
	public static void Main() {
		Stopwatch init = Stopwatch.StartNew();
		using LevelRenderer renderer = new LevelRenderer();
		init.Stop();

		Stopwatch load = Stopwatch.StartNew();
		renderer.LoadLevel("LevelEditorProjects/project.txt");
		load.Stop();

		Stopwatch render = Stopwatch.StartNew();
		renderer.Render();
		render.Stop();

		Console.WriteLine($"Init: {init.ElapsedMilliseconds} ms");
		Console.WriteLine($"Load: {load.ElapsedMilliseconds} ms");
		Console.WriteLine($"Render: {render.ElapsedMilliseconds} ms");
	}
}