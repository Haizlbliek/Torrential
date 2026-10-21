using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Color {
	public byte R;
	public byte G;
	public byte B;
	public byte A;

	public Color(byte r, byte g, byte b, byte a) => (this.R, this.G, this.B, this.A) = (r, g, b, a);
}
