Shader "Hidden/ShadowVolume/Drawing"
{
	Properties
	{
	}

	CGINCLUDE

	#include "UnityCG.cginc"

	struct appdata
	{
		float4 vertex : POSITION;
		float2 uv : TEXCOORD0;
	};

	struct v2f
	{
		float2 uv : TEXCOORD0;
		float4 vertex : SV_POSITION;
		float fade : TEXCOORD1;
	};
	
	uniform fixed4 _ShadowColor;
	uniform sampler2D _ShadowVolumeRT;
	uniform sampler2D _ShadowVolumeFadeRT;
	uniform sampler2D _ShadowVolumeColorRT;
	uniform float2 _ShadowVolumeDistance;/*x:ShadowDistance, y:FadeLength*/

	v2f vert_sv_stencil (appdata v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.vertex = UnityObjectToClipPos(v.vertex);
		return o;
	}
	
	fixed4 frag_sv_stencil (v2f i) : SV_Target
	{
		return fixed4(0,0,0,0);
	}

	v2f vert_sv_stencil_fade (appdata v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.vertex = UnityObjectToClipPos(v.vertex);
		
		float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		/*
		|---&--|--------------|
		a   d  b              c
		shadow distance = ac
		fade length = ab
		*/
		float d_c = length(wPos - _WorldSpaceCameraPos);
		float shadowDistance = _ShadowVolumeDistance.x;
		float fadeLength = _ShadowVolumeDistance.y;
		float b_c = shadowDistance - fadeLength;
		float d_b = max(d_c - b_c, 0);
		float fade = d_b / fadeLength;
		o.fade = fade;

		return o;
	}

	fixed4 frag_sv_stencil_fade (v2f i) : SV_Target
	{
		return fixed4(0,0,0,i.fade);
	}

	v2f vert_shadow(appdata v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.vertex = v.vertex;

		if (_ProjectionParams.x < 0)
		{
			o.vertex.y *= -1;
		}

		return o;
	}

	fixed4 frag_shadow(v2f i) : SV_Target
	{
		return _ShadowColor;
	}

	v2f vert_shadow_fade(appdata v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.vertex = v.vertex;
		o.uv = v.uv;

		if (_ProjectionParams.x < 0)
		{
			o.vertex.y *= -1;
		}

		return o;
	}

	fixed4 frag_shadow_fade(v2f i) : SV_Target
	{
		fixed4 fade = tex2D(_ShadowVolumeFadeRT, i.uv).a;
		fixed4 shadow = _ShadowColor;
		shadow.rgb += fade; 
		return shadow;
	}

	v2f vert_overlay_shadow (appdata v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.vertex = UnityObjectToClipPos(v.vertex);
		o.uv = v.uv;
		return o;
	}

	fixed4 frag_overlay_shadow(v2f i) : SV_Target
	{
		fixed4 shadow = tex2D(_ShadowVolumeRT, i.uv);
		fixed4 color = tex2D(_ShadowVolumeColorRT, i.uv);

		// Bright dark area in the shadow
		/*
		fixed s = shadow.r < 0.8 ? 1 : 0;

		fixed gray = (color.r + color.g + color.b) * 0.3333;
		gray -= 0.4;// dark controller
		gray = saturate(gray) * s + 1;

		return shadow * color * gray;
		*/

		return shadow * color;
	}

	ENDCG

	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		// ZFail
		// Pass 0
		Pass
		{
			Stencil{
				Ref 1
				Comp Always
				ZFail IncrSat
			}
			ZWrite Off
			ColorMask 0
			Cull Front
			CGPROGRAM
			#pragma vertex vert_sv_stencil
			#pragma fragment frag_sv_stencil
			ENDCG
		}
		// Pass 1
		Pass
		{
			Stencil{
				Ref 1
				Comp Always
				ZFail DecrSat
			}
			ZWrite Off
			ColorMask 0
			Cull Back
			CGPROGRAM
			#pragma vertex vert_sv_stencil
			#pragma fragment frag_sv_stencil
			ENDCG
		}


		// ZPass
		// Pass 0
		// Pass
		// {
		//  	Stencil{
		//  		Ref 1
		//  		Comp Always
		//  		Pass IncrSat
		//  	}
		//  	ZWrite Off
		//  	ColorMask 0
		//  	Cull Back
		//  	CGPROGRAM
		//  	#pragma vertex vert_sv_stencil
		//  	#pragma fragment frag_sv_stencil
		//  	ENDCG
		//  }
		// Pass 1
		// Pass
		// {
		//  	Stencil{
		//  		Ref 1
		//  		Comp Always
		//  		Pass DecrSat
		//  	}
		//  	ZWrite Off
		//  	ColorMask 0
		//  	Cull Front
		//  	CGPROGRAM
		//  	#pragma vertex vert_sv_stencil
		//  	#pragma fragment frag_sv_stencil
		//  	ENDCG
		// }


		// Draw Shadow
		// Pass 2
		Pass
		{
			Stencil{
				Ref 0
				Comp NotEqual
				Pass Keep
			}
			Blend Zero SrcColor
			ZWrite Off
			ColorMask RGB
			Cull Back
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_shadow
			#pragma fragment frag_shadow
			ENDCG
		}

		// Clear Stencil Buffer
		// Pass 3
		Pass
		{
			Stencil{
				Ref 0
				Comp Always
				Pass Replace
			}
			ColorMask 0
			Cull Back
			ZWrite Off
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_shadow
			#pragma fragment frag_shadow
			ENDCG
		}

		// Two-Side Stencil
		// Pass 4
		Pass
		{
			Stencil{
				Ref 1
				Comp Always
				ZFailBack IncrWrap
				ZFailFront DecrWrap
			}
			ZWrite Off
			ColorMask 0
			Cull Off
			CGPROGRAM
			#pragma vertex vert_sv_stencil
			#pragma fragment frag_sv_stencil
			ENDCG
		}

		// RenderTexture Composite
		// Overlay shadow on the color
		// Pass 5
		Pass
		{
			ZWrite Off
			ColorMask RGB
			Cull Back
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_overlay_shadow
			#pragma fragment frag_overlay_shadow
			ENDCG
		}

		// RenderTexture Composite
		// Draw Shadow
		// Pass 6
		Pass
		{
			Stencil{
				Ref 0
				Comp NotEqual
				Pass Keep
			}
			ZWrite Off
			ColorMask RGB
			Cull Back
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_shadow
			#pragma fragment frag_shadow
			ENDCG
		}

		// ZFail(Shadow Fade)
		// Pass 7
		Pass
		{
			Stencil{
				Ref 1
				Comp Always
				ZFail IncrSat
			}
			ZWrite Off
			ColorMask A
			Cull Front
			CGPROGRAM
			#pragma vertex vert_sv_stencil_fade
			#pragma fragment frag_sv_stencil_fade
			ENDCG
		}
		// Pass 8
		Pass
		{
			Stencil{
				Ref 1
				Comp Always
				ZFail DecrSat
			}
			ZWrite Off
			ColorMask A
			Cull Back
			CGPROGRAM
			#pragma vertex vert_sv_stencil_fade
			#pragma fragment frag_sv_stencil_fade
			ENDCG
		}

		// RenderTexture Composite(Shadow Fade)
		// Draw Shadow
		// Pass 9
		Pass
		{
			Stencil{
				Ref 0
				Comp NotEqual
				Pass Keep
			}
			ZWrite Off
			ColorMask RGB
			Cull Back
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_shadow_fade
			#pragma fragment frag_shadow_fade
			ENDCG
		}
	}
}
