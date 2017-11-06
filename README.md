![Vapor Logo](http://vapor.rustltd.com/img/logo.png)

Vapor is a Volumetric Fog system for Unity to add atmospheric lighting to any scene. It is based on work by [Bart Wronski](https://bartwronski.files.wordpress.com/2014/08/bwronski_volumetric_fog_siggraph2014.pdf) and research from [Frostbite](https://www.slideshare.net/DICEStudio/physically-based-and-unified-volumetric-rendering-in-frostbite)
It was originally written to be an asset store asset, but as it never quite panned out it is now released open source with a MIT license!

**Features**

- Physically based fog scattering
- Supports shadowed directional and spot lights, and unshadowed point lights.
- Smooth UX integrated into unity
- Performance minded
- Optimized for Single Pass VR (<1ms on a 980 TI on a vive)


![Vapor Pillars](http://g2f.nl/0dsh5mz)


**Setup**

To use Vapor add the Vapor component to your main camera. Unity lights that you want to scatter into the fog should have a vapor light component.

**Presets**

Vapor Presets are a collection of physical fog settings to easily switch between fog settings. A few presets are included by default for different types of fog. Vapor Zones also link to a preset for their physical fog settings.

![Sponza](http://g2f.nl/0552cy4)

**Scripting**

The Vapor preset system is also used for lerping between different settings. Every vapor component stores two settings: vapor.Setting and vapor.BlendToSetting. Settings are lerped between these two using vapor.BlendTime.
If you need zones with different fog settings (eg. when you have different fog settings a cave or the inside of a house) it is recommended to use this scripting approach if possible as opposed to Vapor Zones as zones occur an additional overhead.

**Performance**

Vapor uses a volumetric fog texture to render the fog into. The resolution of this texture is the determining factor for Vapor's performance. In forward rendering shaders directly sample from this volume texture, in deferred an additional fullscreen pass is done to apply the fog. 

**Limitations**

Vapor currently works best with deferred rendering. To use vapor on Transparent objects or in Forward rendering mode, shaders should use the VaporStandard shader. Additionally, all Alloy Shader Framework shaders support Vapor. If you use your own custom shaders you will need to integrate the Vapor code into these shaders, see Vapor.cginc for more details. Integrating into custom surface shaders is not supported t the moment.

Vapor uses a volume texture that is lower than the resolution of the screen. This can cause some aliasing artifacts particularly in shadow borders. To alleviate this Vapor uses temporal anti aliasing on the volume texture, but this can cause streaking artifacts. There are parameters to control the tradeoff of these different artifacts.

Vapor renders fog up until the far plane. The further the far plane is, the more coarse the fog will be calculated. Currently there is no way to set a seperate far plane for Vapor and your camera. The distribution of voxels used nearby / far away can be set by the 'depth curve power' parameter.

**Inspector** 

Each field has a tooltip describing it's function. The UX has been designed to make it easy to set up consistent looking fog.

![Inspector](http://g2f.nl/0kgx1ek)

**Roadmap**

There are lots of performance improvments to still be done. There are also some problems with the temportal filtering to be adressed. Long term the goal is to have Vapor simulate nearby fog with high fidelity, while still being accurate enough to produce a physically based skybox in the distance.

**Contributing**

Any contributions are greatly appreciated! Feel free to raise issues, and any pull requests will be reviewed.

**Credits**

Thanks to everyone at RUST ltd for making this release happen.
- Anton Hand - UX Advise, testing
- Joshua Ols - Alloy integration and shader advice
- Luke Noonan & Lucas Miller - Marketing, logo and website

http://vapor.rustltd.com/
