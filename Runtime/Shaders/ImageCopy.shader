// Copies a texture (or a region of it) into a region of an image atlas page
// (ImageAtlas.cs), through Blit: the Blit quad (0..1) is moved onto the
// destination region, so only its texels are written. The result is
// premultiplied (clean bilinear edges); shrinking takes four taps around each
// texel at a mip level half the shrink, a box filter even without mips.
Shader "MenUI/ImageCopy"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _DestinationRect;   // left, bottom, right, top in the page's uv
            float4 _SourceRect;        // left, bottom, right, top in the source's uv
            float4 _Footprint;         // source uv per copied texel (x, y)
            float _LevelOfDetail;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float4 vertex = input.vertex;
                vertex.xy = lerp(_DestinationRect.xy, _DestinationRect.zw, input.vertex.xy);
                output.position = UnityObjectToClipPos(vertex);
                output.uv = lerp(_SourceRect.xy, _SourceRect.zw, input.uv);
                return output;
            }

            float4 Tap(float2 uv)
            {
                // Inside the region: a sprite never picks up its neighbours.
                float2 margin = _MainTex_TexelSize.xy * 0.5;
                uv = clamp(uv, _SourceRect.xy + margin, _SourceRect.zw - margin);
                float4 color = tex2Dlod(_MainTex, float4(uv, 0, _LevelOfDetail));
                return float4(color.rgb * color.a, color.a);
            }

            float4 frag(v2f input) : SV_Target
            {
                float2 d = _Footprint.xy * 0.25;
                return (Tap(input.uv + float2(-d.x, -d.y)) + Tap(input.uv + float2(d.x, -d.y))
                    + Tap(input.uv + float2(-d.x, d.y)) + Tap(input.uv + float2(d.x, d.y))) * 0.25;
            }
            ENDCG
        }
    }
}
