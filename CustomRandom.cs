public static class CustomRandom {
	public static uint Get(uint seed, uint x, uint y, uint z) {
		unchecked {
			uint h = seed;

			h ^= x * 0x9E3779B9u;
			h ^= y * 0x85EBCA6Bu;
			h ^= z * 0xC2B2AE35u;

			h ^= h >> 16;
			h *= 0x7FEB352Du;
			h ^= h >> 15;
			h *= 0x846CA68Bu;
			h ^= h >> 16;

			return h;
		}
	}
}