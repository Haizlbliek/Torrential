#version 450

layout(location = 0) in uint inSrcX;
layout(location = 1) in uint inSrcY;
layout(location = 2) in uint inSrcWidth;
layout(location = 3) in uint inSrcHeight;
layout(location = 4) in uint inSrcPage;
layout(location = 5) in int inDestX;
layout(location = 6) in int inDestY;
layout(location = 7) in uint inDestLayer;

layout(location = 0) out vec3 outAtlasUV;

const vec2 TargetSize = vec2(1400.0, 800.0);
const vec2 AtlasSize = vec2(8192.0, 8192.0);

void main() {
	vec2 corner = vec2(
		float(gl_VertexIndex & 1),
		float((gl_VertexIndex >> 1) & 1)
	);

	vec2 size = vec2(float(inSrcWidth), float(inSrcHeight));
	vec2 pixelSrc = vec2(float(inSrcX), float(inSrcY)) + corner * size;
	vec2 pixelDest = vec2(float(inDestX), float(inDestY)) + corner * size;
	vec2 clipSpace = pixelDest / TargetSize * 2.0 - 1.0;

	outAtlasUV = vec3(pixelSrc / AtlasSize, float(inSrcPage));
	gl_Position = vec4(clipSpace.x, -clipSpace.y, 0.0, 1.0);
}