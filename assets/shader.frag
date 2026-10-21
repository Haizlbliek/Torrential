#version 450

layout(location = 0) in vec3 outAtlasUV;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler PointSampler;
layout(set = 0, binding = 1) uniform texture2DArray MainTextures;

void main() {
	ivec3 texelCoords = ivec3(
		int(outAtlasUV.x * 8192.0),
		int(outAtlasUV.y * 8192.0),
		int(outAtlasUV.z)
	);

	vec4 color = texelFetch(
		sampler2DArray(MainTextures, PointSampler),
		texelCoords,
		0
	);

	if (color.r > 0.99 && color.g > 0.99 && color.b > 0.99)
		discard;

	outColor = color;
}