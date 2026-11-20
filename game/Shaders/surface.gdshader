// Vertex-coloured batches (walls, furniture, equipment, people) with surface detail. UV.x carries the
// kind of surface (SurfaceKind); the detail multiplies the painted colour, so tints keep working.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/common.gdshaderinc"

varying vec3 wpos;
varying vec3 wnrm;
varying flat float kind;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	kind = UV.x;
}

void fragment() {
	int id = clamp(int(kind + 0.5), 0, 13);
	vec3 base = bh_to_linear(COLOR.rgb);
	if (id == 0) {
		ALBEDO = base;
		ROUGHNESS = 0.85;
	} else {
		vec4 d;
		vec3 n;
		bh_triplanar(wpos, wnrm, id, d, n);
		ALBEDO = base * (0.75 + 0.5 * d.r);
		ROUGHNESS = d.g;
		METALLIC = id == 4 ? 0.45 : 0.0;
		SPECULAR = id == 9 ? 0.7 : 0.5;
		NORMAL_MAP = n;
	}
}
