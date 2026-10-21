using System.Diagnostics.CodeAnalysis;

public struct Vector2i : IEquatable<Vector2i> {
	public int X;
	public int Y;

	public Vector2i(int x, int y) => (this.X, this.Y) = (x, y);

	public readonly bool Equals(Vector2i other) => other.X == this.X && other.Y == this.Y;
	public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Vector2i other && other.Equals(this);
	public override readonly int GetHashCode() => HashCode.Combine(this.X, this.Y);
}