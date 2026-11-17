// The painted floors (FloorPainter) with each tile's finish laid over them: sealed vinyl, safety vinyl,
// wood planks, terrazzo, carpet tiles or ceramic tiles. The finish map holds one texel per tile.
shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/common.gdshaderinc"

uniform sampler2D painted : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D finishes : filter_nearest;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 c = texture(painted, UV);
	ivec2 size = textureSize(finishes, 0);
	int id = int(texelFetch(finishes, clamp(ivec2(UV * vec2(size)), ivec2(0), size - 1), 0).r * 255.0 + 0.5);
	id = clamp(id, 0, 13);
	vec2 q = wpos.xz * KIND_SCALE[id];
	if (id == 2) {
		q = vec2(wpos.x * 0.3, wpos.z * 1.4); // planks: grain along x
	}
	vec4 d = texture(detail, vec3(q, float(id)));
	// Broad, soft wear across hard floors so big rooms don't look printed.
	float wear = texture(detail, vec3(wpos.xz * 0.06, 5.0)).r;
	float soft = (id == 2 || id == 10) ? 1.0 : (0.95 + 0.1 * wear);
	ALBEDO = c.rgb * (0.75 + 0.5 * d.r) * soft;
	ALPHA = c.a;
	ROUGHNESS = d.g;
	SPECULAR = id == 10 ? 0.3 : 0.55;
	NORMAL_MAP = texture(normals, vec3(q, float(id))).rgb;
	NORMAL_MAP_DEPTH = 0.6;
}
