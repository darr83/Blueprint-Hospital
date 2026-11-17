// Wall finishes, pictures and signs from the decor atlas. UV2.x says what each quad is made of
// (paint, glazed tiles, wood panelling; signs and pictures stay plain).
shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/common.gdshaderinc"

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic;

varying vec3 wpos;
varying vec3 wnrm;
varying flat float kind;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	kind = UV2.x;
}

void fragment() {
	vec4 c = texture(atlas, UV);
	int id = clamp(int(kind + 0.5), 0, 13);
	ALPHA = c.a;
	if (id == 0) {
		ALBEDO = c.rgb;
		ROUGHNESS = 0.8;
	} else {
		vec4 d;
		vec3 n;
		bh_triplanar(wpos, wnrm, id, d, n);
		ALBEDO = c.rgb * (0.75 + 0.5 * d.r);
		ROUGHNESS = d.g;
		SPECULAR = id == 9 ? 0.7 : 0.5;
		NORMAL_MAP = n;
	}
}
