using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlitInstance {
	public uint SrcX;
	public uint SrcY;
	public uint SrcWidth;
	public uint SrcHeight;
	public uint SrcPage;

	public int DestX;
	public int DestY;
	public uint DestLayer;

	public BlitInstance(uint srcX, uint srcY, uint srcWidth, uint srcHeight, uint srcPage, int destX, int destY, uint destLayer) {
		this.SrcX = srcX;
		this.SrcY = srcY;
		this.SrcWidth = srcWidth;
		this.SrcHeight = srcHeight;
		this.SrcPage = srcPage;
		this.DestX = destX;
		this.DestY = destY;
		this.DestLayer = destLayer;
	}
}
