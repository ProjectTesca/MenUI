// MenUI's ubershader: every built-in primitive kind, one draw per panel.
// The record layout is documented in PanelRenderer.cs.
Shader "MenUI/Panel"
{
    Properties
    {
        _RecordTexture ("Record Texture", 2D) = "black" {}
        _Atlas0 ("Atlas 0", 2D) = "black" {}
        _Atlas1 ("Atlas 1", 2D) = "black" {}
        _Atlas2 ("Atlas 2", 2D) = "black" {}
        _Atlas3 ("Atlas 3", 2D) = "black" {}
        _Atlas4 ("Atlas 4", 2D) = "black" {}
        _Atlas5 ("Atlas 5", 2D) = "black" {}
        _Atlas6 ("Atlas 6", 2D) = "black" {}
        _Atlas7 ("Atlas 7", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define KIND_ROUNDED_RECTANGLE 1
            #define KIND_GLYPH 2
            #define KIND_OUTER_SHADOW 3
            #define KIND_INSET_SHADOW 4
            #define KIND_IMAGE 5
            #define KIND_RIPPLE 6
            #define KIND_SHAPE 7
            #define KIND_ARC 8
            #define KIND_WAVE 9
            #define GROUP_STRIDE 16
            #define GROUP_CAPACITY 128
            #define TAU 6.28318530718

            sampler2D _RecordTexture;
            float4 _RecordTextureSize;   // texels per record, record capacity
            float4 _TargetSize;          // physical px
            float _RecordCount;
            // Transform groups (TransformGroups.cs): accumulated translation (xy),
            // scale (z) and opacity (w), and screen-space clip (left, top,
            // right, bottom) per group. Records stay in layout space; the
            // fragment works there too (antialiasing scaled by the zoom).
            float4 _GroupOffsets[GROUP_CAPACITY];
            float4 _GroupClips[GROUP_CAPACITY];

            sampler2D _Atlas0;
            sampler2D _Atlas1;
            sampler2D _Atlas2;
            sampler2D _Atlas3;
            sampler2D _Atlas4;
            sampler2D _Atlas5;
            sampler2D _Atlas6;
            sampler2D _Atlas7;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 corner : TEXCOORD0;
                float2 index : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 pixelAndUv : TEXCOORD0;                 // physical px, glyph atlas uv
                nointerpolation float4 color : TEXCOORD1;
                nointerpolation float4 radii : TEXCOORD2;      // tl, tr, br, bl
                nointerpolation float4 parameters : TEXCOORD3; // kind, atlas, sigma, spread
                nointerpolation float4 borderColor : TEXCOORD4;
                nointerpolation float4 extra : TEXCOORD5;      // border widths t r b l | element rect
                nointerpolation float4 shape : TEXCOORD6;      // x, y, width, height
                nointerpolation float4 clip : TEXCOORD7;       // left, top, right, bottom
                nointerpolation float2 group : TEXCOORD8;      // scale, opacity
            };

            float4 FetchTexel(float record, float texel)
            {
                float2 uv = float2((texel + 0.5) / _RecordTextureSize.x, (record + 0.5) / _RecordTextureSize.y);
                return tex2Dlod(_RecordTexture, float4(uv, 0, 0));
            }

            v2f vert(appdata input)
            {
                v2f output = (v2f)0;
                float record = input.index.x;
                float4 parameters = FetchTexel(record, 4);
                if (record >= _RecordCount || parameters.x < 0.5)
                {
                    output.position = float4(0, 0, 0, 1);
                    return output;
                }

                // The kind texel packs kind + GROUP_STRIDE * group.
                float group = floor(parameters.x / GROUP_STRIDE);
                parameters.x -= group * GROUP_STRIDE;
                float4 transform = _GroupOffsets[(int)group];   // translation, scale, opacity
                if (transform.w <= 0.0001)
                {
                    // An invisible group (a prepared box, a finished fade):
                    // a collapsed quad, no pixels shaded.
                    output.position = float4(0, 0, 0, 1);
                    return output;
                }
                float4 groupClip = _GroupClips[(int)group];
                float zoom = max(transform.z, 1e-4);

                float4 quad = FetchTexel(record, 0);
                float2 pixel = quad.xy + input.corner * quad.zw;   // layout space
                float2 screen = pixel * zoom + transform.xy;
                float2 clipSpace = float2(screen.x / _TargetSize.x * 2 - 1, 1 - screen.y / _TargetSize.y * 2);
                clipSpace.y *= _ProjectionParams.x;
                output.position = float4(clipSpace, 0.5, 1);

                float4 third = FetchTexel(record, 3);
                float2 atlasUv = float2(lerp(third.x, third.z, input.corner.x), lerp(third.w, third.y, input.corner.y));
                output.pixelAndUv = float4(pixel, atlasUv);
                output.color = FetchTexel(record, 2);
                output.radii = third;
                output.parameters = parameters;
                output.borderColor = FetchTexel(record, 5);
                output.extra = FetchTexel(record, 6);
                output.shape = FetchTexel(record, 7);
                // The group's clip, brought back into layout space.
                float4 localGroupClip = (groupClip - transform.xyxy) / zoom;
                float4 clip = FetchTexel(record, 1);
                output.clip = float4(max(clip.xy, localGroupClip.xy), min(clip.zw, localGroupClip.zw));
                output.group = float2(zoom, transform.w);
                return output;
            }

            // Signed distance to a rounded rectangle with a radius per corner (y down).
            float RoundedBox(float2 p, float4 rect, float4 radii)
            {
                float2 halfSize = rect.zw * 0.5;
                float2 q = p - (rect.xy + halfSize);
                float r = q.x < 0 ? (q.y < 0 ? radii.x : radii.w) : (q.y < 0 ? radii.y : radii.z);
                r = min(r, min(halfSize.x, halfSize.y));
                float2 d = abs(q) - halfSize + r;
                return length(max(d, 0)) + min(max(d.x, d.y), 0) - r;
            }

            // Abramowitz-Stegun approximation of erf.
            float Erf(float x)
            {
                float s = sign(x);
                float a = abs(x);
                float t = 1 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
                t *= t;
                return s - s / (t * t);
            }

            // Coverage of a blurred shape at signed distance d (Gaussian edge).
            float BlurredCoverage(float d, float sigma)
            {
                return sigma > 0.01 ? 0.5 - 0.5 * Erf(d / (sigma * 1.41421356)) : saturate(0.5 - d);
            }

            // Glyph atlases of every font (MenUIContext.AtlasSlot), by slot.
            float SampleAtlas(float index, float2 uv)
            {
                if (index < 3.5)
                {
                    if (index < 0.5) return tex2D(_Atlas0, uv).a;
                    if (index < 1.5) return tex2D(_Atlas1, uv).a;
                    if (index < 2.5) return tex2D(_Atlas2, uv).a;
                    return tex2D(_Atlas3, uv).a;
                }
                if (index < 4.5) return tex2D(_Atlas4, uv).a;
                if (index < 5.5) return tex2D(_Atlas5, uv).a;
                if (index < 6.5) return tex2D(_Atlas6, uv).a;
                return tex2D(_Atlas7, uv).a;
            }

            // Coverage of a signed distance field antialiased by its own
            // screen-space rate of change (any scale, any shape).
            float FieldCoverage(float d)
            {
                return saturate(0.5 - d / max(fwidth(d), 1e-4));
            }

            float2 Rotate(float2 p, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
            }

            // One shape of the morphing family (MenUI.ShapeSpec), centered at
            // the origin, fitting radius `radius`. `spec`: kind, count, amount,
            // rotation. Approximate distance: antialiased with FieldCoverage.
            float ShapeDistance(float2 p, float radius, float4 spec)
            {
                float kind = spec.x;
                float n = spec.y;
                float amount = spec.z;
                p = Rotate(p, spec.w);
                float len = length(p);
                float angle = atan2(p.x, -p.y);
                if (kind < 0.5)
                {
                    // Scalloped (cookie, clover, flower): lobes of cosine depth.
                    float r = radius * (1 - amount * (0.5 - 0.5 * cos(n * angle)));
                    return len - r;
                }
                if (kind < 1.5)
                {
                    // Rays (sunny, burst): pointed tips, rounded valleys.
                    float wave = abs(sin(n * angle * 0.5));
                    float r = radius * (1 - amount * sqrt(wave));
                    return len - r;
                }
                if (kind < 2.5)
                {
                    // Rounded regular polygon (triangle, square, pentagon ...),
                    // a corner at the top; `amount`: corner rounding 0..1.
                    float sector = TAU / n;
                    float a = fmod(angle + sector * 0.5 + TAU * 4, sector) - sector * 0.5;
                    float2 corner = float2(sin(a), cos(a)) * len;     // corner along +y
                    float rounding = amount * radius * 0.6;
                    float apothem = (radius - rounding) * cos(sector * 0.5);
                    // Distance to the edge line x*sin(h) + y*cos(h) = apothem, h = half sector.
                    float h = sector * 0.5;
                    float2 edgeNormal = float2(sin(h), cos(h));
                    float2 edgeNormal2 = float2(-sin(h), cos(h));
                    float d = max(dot(corner, edgeNormal), dot(corner, edgeNormal2)) - apothem;
                    return d - rounding;
                }
                if (kind < 3.5)
                {
                    // Oval: `amount` is height / width.
                    float2 axes = float2(radius, radius * max(amount, 0.05));
                    return (length(p / axes) - 1) * min(axes.x, axes.y);
                }
                // Pill: a horizontal capsule, `amount` is height / width.
                float halfHeight = radius * max(amount, 0.05);
                float2 capsule = float2(max(abs(p.x) - (radius - halfHeight), 0), p.y);
                return length(capsule) - halfHeight;
            }

            // A point on an arc (angle 0 at the top, clockwise) of a wavy radius.
            float ArcRadius(float angle, float radius, float4 wave)
            {
                return radius + wave.x * sin(wave.y * angle + wave.z);
            }

            float4 Shade(v2f input)
            {
                float2 p = input.pixelAndUv.xy;
                if (p.x < input.clip.x || p.y < input.clip.y || p.x >= input.clip.z || p.y >= input.clip.w)
                {
                    discard;
                }

                float kind = input.parameters.x;
                float zoom = input.group.x;
                float4 fill = float4(input.color.rgb * input.color.a, input.color.a);

                if (kind < KIND_ROUNDED_RECTANGLE + 0.5)
                {
                    float outer = RoundedBox(p, input.shape, input.radii);
                    float coverage = saturate(0.5 - outer * zoom);
                    float4 widths = input.extra;   // top, right, bottom, left
                    float4 result = fill;
                    if (widths.x + widths.y + widths.z + widths.w > 0)
                    {
                        float4 innerRect = float4(input.shape.x + widths.w, input.shape.y + widths.x,
                            input.shape.z - widths.w - widths.y, input.shape.w - widths.x - widths.z);
                        float4 innerRadii = max(0, input.radii - float4(max(widths.w, widths.x), max(widths.y, widths.x),
                            max(widths.y, widths.z), max(widths.w, widths.z)));
                        float inside = (innerRect.z > 0 && innerRect.w > 0)
                            ? saturate(0.5 - RoundedBox(p, innerRect, innerRadii) * zoom) : 0;
                        float4 border = float4(input.borderColor.rgb * input.borderColor.a, input.borderColor.a);
                        result = fill * inside + border * (1 - inside);
                    }
                    return result * coverage;
                }
                if (kind < KIND_GLYPH + 0.5)
                {
                    float field = SampleAtlas(input.parameters.y, input.pixelAndUv.zw);
                    float width = max(fwidth(field), 1e-4);
                    return fill * saturate((field - 0.5) / width + 0.5);
                }
                if (kind < KIND_OUTER_SHADOW + 0.5)
                {
                    float spread = input.parameters.w;
                    float4 shadowRadii = input.radii > 0 ? max(0, input.radii + spread) : 0;
                    float shadow = BlurredCoverage(RoundedBox(p, input.shape, shadowRadii) * zoom, input.parameters.z * zoom);
                    // Nothing under the element itself (CSS clips the shadow to outside the border box).
                    float outside = saturate(0.5 + RoundedBox(p, input.extra, input.radii) * zoom);
                    return fill * shadow * outside;
                }
                if (kind < KIND_INSET_SHADOW + 0.5)
                {
                    float spread = input.parameters.w;
                    float inside = saturate(0.5 - RoundedBox(p, input.extra, input.radii) * zoom);
                    float4 innerRadii = max(0, input.radii - spread);
                    float hole = BlurredCoverage(RoundedBox(p, input.shape, innerRadii) * zoom, input.parameters.z * zoom);
                    return fill * inside * (1 - hole);
                }
                if (kind < KIND_IMAGE + 0.5)
                {
                    return 0;
                }
                if (kind < KIND_RIPPLE + 0.5)
                {
                    // A circle (extra: center, radius, soft edge) inside the
                    // element's rounded box (shape, radii).
                    float distance = length(p - input.extra.xy) - input.extra.z;
                    float edge = max(input.extra.w, 1 / zoom);
                    float circle = saturate(0.5 - distance / edge);
                    float box = saturate(0.5 - RoundedBox(p, input.shape, input.radii) * zoom);
                    return fill * circle * box;
                }
                if (kind < KIND_SHAPE + 0.5)
                {
                    // Morph between shapes A (radii texel) and B (extra) by
                    // borderColor.x, the whole turned by borderColor.y.
                    float2 center = input.shape.xy + input.shape.zw * 0.5;
                    float radius = min(input.shape.z, input.shape.w) * 0.5;
                    float2 q = Rotate(p - center, input.borderColor.y);
                    float t = input.borderColor.x;
                    float d = ShapeDistance(q, radius, input.radii);
                    if (abs(t) > 0.001)
                    {
                        d = lerp(d, ShapeDistance(q, radius, input.extra), t);
                    }
                    return fill * FieldCoverage(d);
                }
                if (kind < KIND_ARC + 0.5)
                {
                    // A stroked arc with round caps around the shape's center:
                    // extra = radius, thickness, start, sweep (radians, from the
                    // top, clockwise); radii = wave amplitude, count, phase.
                    float2 center = input.shape.xy + input.shape.zw * 0.5;
                    float2 q = p - center;
                    float radius = input.extra.x;
                    float halfThickness = input.extra.y * 0.5;
                    float start = input.extra.z;
                    float sweep = input.extra.w;
                    float angle = atan2(q.x, -q.y);
                    float relative = angle - start;
                    relative -= TAU * floor(relative / TAU);   // 0 .. TAU
                    float d;
                    if (sweep >= TAU - 0.0001 || relative <= sweep)
                    {
                        d = abs(length(q) - ArcRadius(start + relative, radius, input.radii)) - halfThickness;
                    }
                    else
                    {
                        float endAngle = start + sweep;
                        float2 a = float2(sin(start), -cos(start)) * ArcRadius(start, radius, input.radii);
                        float2 b = float2(sin(endAngle), -cos(endAngle)) * ArcRadius(endAngle, radius, input.radii);
                        d = min(length(q - a), length(q - b)) - halfThickness;
                    }
                    return fill * FieldCoverage(d);
                }
                if (kind < KIND_WAVE + 0.5)
                {
                    // A stroked horizontal line with round caps: extra = x0, x1,
                    // y, thickness; radii = amplitude, wavelength, phase.
                    float x0 = input.extra.x;
                    float x1 = input.extra.y;
                    float halfThickness = input.extra.w * 0.5;
                    float amplitude = input.radii.x;
                    float k = input.radii.y > 0 ? TAU / input.radii.y : 0;
                    float phase = input.radii.z;
                    float x = clamp(p.x, x0, x1);
                    float y = input.extra.z + amplitude * sin(k * (x - x0) + phase);
                    float d;
                    if (p.x < x0 || p.x > x1)
                    {
                        d = length(p - float2(x, y)) - halfThickness;
                    }
                    else
                    {
                        float slope = amplitude * k * cos(k * (x - x0) + phase);
                        d = abs(p.y - y) / sqrt(1 + slope * slope) - halfThickness;
                    }
                    return fill * FieldCoverage(d);
                }
                return 0;
            }

            float4 frag(v2f input) : SV_Target
            {
                // The group's opacity (a fading element and what is in it).
                return Shade(input) * input.group.y;
            }
            ENDCG
        }
    }
}
