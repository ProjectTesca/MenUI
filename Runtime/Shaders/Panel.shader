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
            #define GROUP_STRIDE 8
            #define GROUP_CAPACITY 128

            sampler2D _RecordTexture;
            float4 _RecordTextureSize;   // texels per record, record capacity
            float4 _TargetSize;          // physical px
            float _RecordCount;
            // Transform groups (TransformGroups.cs): accumulated offset (xy) and
            // screen-space clip (left, top, right, bottom) per group.
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
                float2 offset = _GroupOffsets[(int)group].xy;
                float4 groupClip = _GroupClips[(int)group];

                float4 quad = FetchTexel(record, 0);
                float2 pixel = quad.xy + input.corner * quad.zw + offset;
                float2 clipSpace = float2(pixel.x / _TargetSize.x * 2 - 1, 1 - pixel.y / _TargetSize.y * 2);
                clipSpace.y *= _ProjectionParams.x;
                output.position = float4(clipSpace, 0.5, 1);

                float4 third = FetchTexel(record, 3);
                float2 atlasUv = float2(lerp(third.x, third.z, input.corner.x), lerp(third.w, third.y, input.corner.y));
                output.pixelAndUv = float4(pixel, atlasUv);
                output.color = FetchTexel(record, 2);
                output.radii = third;
                output.parameters = parameters;
                output.borderColor = FetchTexel(record, 5);
                float4 extra = FetchTexel(record, 6);
                if (parameters.x > KIND_GLYPH + 0.5)
                {
                    extra.xy += offset;   // shadows: the element's rectangle
                }
                output.extra = extra;
                float4 shape = FetchTexel(record, 7);
                shape.xy += offset;
                output.shape = shape;
                float4 clip = FetchTexel(record, 1) + offset.xyxy;
                output.clip = float4(max(clip.xy, groupClip.xy), min(clip.zw, groupClip.zw));
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

            float4 frag(v2f input) : SV_Target
            {
                float2 p = input.pixelAndUv.xy;
                if (p.x < input.clip.x || p.y < input.clip.y || p.x >= input.clip.z || p.y >= input.clip.w)
                {
                    discard;
                }

                float kind = input.parameters.x;
                float4 fill = float4(input.color.rgb * input.color.a, input.color.a);

                if (kind < KIND_ROUNDED_RECTANGLE + 0.5)
                {
                    float outer = RoundedBox(p, input.shape, input.radii);
                    float coverage = saturate(0.5 - outer);
                    float4 widths = input.extra;   // top, right, bottom, left
                    float4 result = fill;
                    if (widths.x + widths.y + widths.z + widths.w > 0)
                    {
                        float4 innerRect = float4(input.shape.x + widths.w, input.shape.y + widths.x,
                            input.shape.z - widths.w - widths.y, input.shape.w - widths.x - widths.z);
                        float4 innerRadii = max(0, input.radii - float4(max(widths.w, widths.x), max(widths.y, widths.x),
                            max(widths.y, widths.z), max(widths.w, widths.z)));
                        float inside = (innerRect.z > 0 && innerRect.w > 0)
                            ? saturate(0.5 - RoundedBox(p, innerRect, innerRadii)) : 0;
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
                    float shadow = BlurredCoverage(RoundedBox(p, input.shape, shadowRadii), input.parameters.z);
                    // Nothing under the element itself (CSS clips the shadow to outside the border box).
                    float outside = saturate(0.5 + RoundedBox(p, input.extra, input.radii));
                    return fill * shadow * outside;
                }
                if (kind < KIND_INSET_SHADOW + 0.5)
                {
                    float spread = input.parameters.w;
                    float inside = saturate(0.5 - RoundedBox(p, input.extra, input.radii));
                    float4 innerRadii = max(0, input.radii - spread);
                    float hole = BlurredCoverage(RoundedBox(p, input.shape, innerRadii), input.parameters.z);
                    return fill * inside * (1 - hole);
                }
                return 0;
            }
            ENDCG
        }
    }
}
