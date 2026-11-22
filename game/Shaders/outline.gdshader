// Thin dark outlines over the 3D view: a full-screen quad on the camera that darkens pixels where the
// depth or the facing of the surface jumps (the edges of walls, furniture, beds and people).
shader_type spatial;
render_mode unshaded, fog_disabled, depth_test_disabled, depth_draw_never, cull_disabled, blend_mix, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D normal_tex : hint_normal_roughness_texture, filter_nearest;
uniform float strength = 0.75;
uniform vec3 ink : source_color = vec3(0.16, 0.19, 0.23);

void vertex() {
	POSITION = vec4(VERTEX.xy * 2.0, 1.0, 1.0);
}

float view_depth(vec2 uv, mat4 inv) {
	float d = texture(depth_tex, uv).r;
	vec4 v = inv * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

void fragment() {
	vec2 px = 1.0 / VIEWPORT_SIZE;
	float c = view_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
	vec3 n = texture(normal_tex, SCREEN_UV).xyz * 2.0 - 1.0;
	float edge = 0.0;
	vec2 offsets[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
	for (int i = 0; i < 4; i++) {
		vec2 uv = SCREEN_UV + offsets[i];
		edge = max(edge, smoothstep(0.08, 0.2, abs(view_depth(uv, INV_PROJECTION_MATRIX) - c)));
		vec3 m = texture(normal_tex, uv).xyz * 2.0 - 1.0;
		edge = max(edge, smoothstep(0.75, 0.45, dot(n, m)) * 0.8);
	}
	ALBEDO = ink;
	ALPHA = edge * strength;
}
