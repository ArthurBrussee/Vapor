# Vapor
Vapor is a Volumetric Fog system for Unity based on work by [Bart Wronski](https://bartwronski.files.wordpress.com/2014/08/bwronski_volumetric_fog_siggraph2014.pdf) and research from [Frostbite](https://www.slideshare.net/DICEStudio/physically-based-and-unified-volumetric-rendering-in-frostbite)
It was originally written to be an asset store asset, but as it never quite panned out it is now released under an MIT license on github!

**Features**

- Physically based fog scattering
- Supports shadowed directional and spot lights, and unshadowed point lights.
- Smooth UX integrated into unity
- Performance minded
- Optimized for Single Pass VR (<1ms on a 980 TI on a vive)

**Setup**

To use Vapor add the Vapor component to your main camera. Unity lights that should scatter into the fog should have a vapor light component.

**Presets**

Vapor Presets are a collection of physical fog settings to easily switch between fog settings. A few presets are included by default for different types of fog. Vapor Zones also link to a preset for their physical fog settings.

**Scripting**

The Vapor preset system is also used for lerping between different settings. Every vapor component stores two settings: vapor.Setting and vapor.BlendToSetting. Settings are lerped between these two using vapor.BlendTime.
If you need zones with different fog settings (eg. when you have different fog settings a cave or the inside of a house) it is recommended to use this scripting approach if possible as opposed to Vapor Zones as zones occur an additional overhead.

**Performance**

Vapor uses a volumtric fog texture to render the fog into. The resolution of this texture is the determining factor for Vapor's performance. In forward rendering shaders directly sample from this volume texture, in deferred an additional fullscreen pass is done to apply the fog. 

**Limitations**

Vapor currently works best with deferred rendering. To use vapor on Transparent objects or in Forward rendering mode, shaders should use the VaporStandard shader. Additionally, all Alloy Shader Framework shaders support Vapor. If you use your own custom shaders you will need to integrate the Vapor code into these shaders, see Vapor.cginc for more details. Integrating into custom surface shaders is not supported t the moment.

**Contributing**
Any contributions are greatly appreciated! Feel free to raise issues, and any pull requests will be reviewed.
