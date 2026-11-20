// Shared by the surface shaders: the detail textures (see SurfaceDetail.cs), sampled in world space.
uniform sampler2DArray detail : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normals : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

// Repeats per world unit (a unit is half a metre across), by kind: plain, paint, wood, fabric, metal,
// vinyl, ceiling, laminate, rubber, glaze, carpet, terrazzo, safety, ceramic.
const float KIND_SCALE[14] = float[](1.0, 0.45, 0.6, 2.2, 0.8, 0.5, 0.42, 1.0, 1.5, 1.0, 0.7, 0.4, 0.6, 0.5);

vec3 bh_to_linear(vec3 c) {
	return mix(pow((c + vec3(0.055)) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}

// Detail (shade, roughness, height) and normal for a kind, projected three ways and blended by the normal.
void bh_triplanar(vec3 wpos, vec3 wnrm, int id, out vec4 d, out vec3 n) {
	vec3 p = wpos * KIND_SCALE[id];
	vec3 bw = pow(abs(wnrm), vec3(4.0));
	bw /= (bw.x + bw.y + bw.z);
	float l = float(id);
	d = texture(detail, vec3(p.zy, l)) * bw.x + texture(detail, vec3(p.xz, l)) * bw.y + texture(detail, vec3(p.xy, l)) * bw.z;
	n = texture(normals, vec3(p.zy, l)).rgb * bw.x + texture(normals, vec3(p.xz, l)).rgb * bw.y + texture(normals, vec3(p.xy, l)).rgb * bw.z;
}
