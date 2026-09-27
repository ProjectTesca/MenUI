// Draws every MenUI record in one call. The mesh is a pool of quads whose
// uv holds the corner (0/1, 0/1) and uv2.x the quad index; the vertex shader
// reads that quad's record from _RecordTexture and places it in physical
// pixels of the target, so the camera's matrices are not used at all.
//
// Record layout (one row of 8 RGBAFloat texels per record):
//   texel 0: x, y, width, height          (physical px, y down)
//   texel 1: clip left, top, right, bottom
//   texel 2: color (linear, straight alpha)
//   texel 3: atlas uv left, bottom, right, top (glyphs)
//   texel 4: kind, corner radius, border width, atlas index
//   texel 5: border color
Shader "MenUI/Verification/QuadPool"
{
    Properties
    {
        _RecordTexture ("Record Texture", 2D) = "black" {}
        _Atlas0 ("Atlas 0", 2D) = "black" {}
        _Atlas1 ("Atlas 1", 2D) = "black" {}
        _Atlas2 ("Atlas 2", 2D) = "black" {}
        _Atlas3 ("Atlas 3", 2D) = "black" {}
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

            sampler2D _RecordTexture;
            float4 _RecordTextureSize;   // texels per record, record capacity
            float4 _TargetSize;          // width, height in physical px
            float _RecordCount;

            sampler2D _Atlas0;
            sampler2D _Atlas1;
            sampler2D _Atlas2;
            sampler2D _Atlas3;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 corner : TEXCOORD0;
                float2 index : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 pixel : TEXCOORD0;         // physical px inside the target
                float2 local : TEXCOORD1;         // px from the record's top-left corner
                float2 atlasUv : TEXCOORD2;
                nointerpolation float4 size : TEXCOORD3;     // width, height, corner radius, border width
                nointerpolation float4 color : TEXCOORD4;
                nointerpolation float4 borderColor : TEXCOORD5;
                nointerpolation float4 clip : TEXCOORD6;
                nointerpolation float2 kind : TEXCOORD7;     // kind, atlas index
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
                    output.position = float4(0, 0, 0, 1);   // collapse unused quads
                    return output;
                }

                float4 rectangle = FetchTexel(record, 0);
                float2 pixel = rectangle.xy + input.corner * rectangle.zw;
                float2 clipSpace = float2(pixel.x / _TargetSize.x * 2 - 1, 1 - pixel.y / _TargetSize.y * 2);
                clipSpace.y *= _ProjectionParams.x;   // follow Unity's flip when rendering into a texture

                float4 atlas = FetchTexel(record, 3);

                output.position = float4(clipSpace, 0.5, 1);
                output.pixel = pixel;
                output.local = input.corner * rectangle.zw;
                output.atlasUv = float2(lerp(atlas.x, atlas.z, input.corner.x), lerp(atlas.w, atlas.y, input.corner.y));
                output.size = float4(rectangle.zw, parameters.y, parameters.z);
                output.color = FetchTexel(record, 2);
                output.borderColor = FetchTexel(record, 5);
                output.clip = FetchTexel(record, 1);
                output.kind = parameters.xw;
                return output;
            }

            float RoundedRectangleDistance(float2 local, float2 size, float radius)
            {
                float2 halfSize = size * 0.5;
                float2 q = abs(local - halfSize) - halfSize + radius;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
            }

            float SampleAtlas(float index, float2 uv)
            {
                if (index < 0.5) return tex2D(_Atlas0, uv).a;
                if (index < 1.5) return tex2D(_Atlas1, uv).a;
                if (index < 2.5) return tex2D(_Atlas2, uv).a;
                return tex2D(_Atlas3, uv).a;
            }

            float4 frag(v2f input) : SV_Target
            {
                if (input.pixel.x < input.clip.x || input.pixel.y < input.clip.y
                    || input.pixel.x >= input.clip.z || input.pixel.y >= input.clip.w)
                {
                    discard;
                }

                float4 color;
                if (input.kind.x < 1.5)
                {
                    // rounded rectangle with an inner border, 1px anti-aliasing
                    float distance = RoundedRectangleDistance(input.local, input.size.xy, input.size.z);
                    float coverage = saturate(0.5 - distance);
                    float border = input.size.w;
                    float fill = border > 0 ? saturate(0.5 - (distance + border)) : 1;
                    color = lerp(input.borderColor, input.color, fill);
                    color.a *= coverage;
                }
                else
                {
                    // TextMeshPro SDF glyph: 0.5 is the outline, width from screen derivatives
                    float field = SampleAtlas(input.kind.y, input.atlasUv);
                    float width = max(fwidth(field), 1e-4);
                    color = input.color;
                    color.a *= saturate((field - 0.5) / width + 0.5);
                }

                color.rgb *= color.a;   // premultiplied output
                return color;
            }
            ENDCG
        }
    }
}
