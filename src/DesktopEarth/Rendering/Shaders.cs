namespace DesktopEarth.Rendering;

public static class Shaders
{
    public const string EarthVertex = @"
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vNormal;
out vec2 vTexCoord;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * worldPos;
}
";

    public const string EarthFragment = @"
#version 330 core

in vec3 vNormal;
in vec2 vTexCoord;

uniform sampler2D uDayTexture;
uniform sampler2D uNightTexture;
uniform vec3 uSunDirection;
uniform float uAmbient;
uniform float uNightBrightness;

out vec4 FragColor;

void main()
{
    vec3 normal = normalize(vNormal);
    vec3 sunDir = normalize(uSunDirection);

    float NdotL = dot(normal, sunDir);
    float blend = smoothstep(-0.1, 0.1, NdotL);

    vec3 dayColor = texture(uDayTexture, vTexCoord).rgb;
    vec3 nightColor = texture(uNightTexture, vTexCoord).rgb;

    float dayLight = max(NdotL, 0.0) * 0.8 + uAmbient;
    vec3 litDay = dayColor * dayLight;

    // Night side: city lights with threshold to eliminate ocean/ambient glow.
    // The night texture has low-level noise in oceans and dark areas.
    // We use a luminance threshold + pow curve so only actual bright city
    // lights get amplified, while dim noise stays dark.
    float nightLum = dot(nightColor, vec3(0.299, 0.587, 0.114));
    // Threshold: pixels below ~0.04 luminance are treated as pure dark
    float lightMask = smoothstep(0.02, 0.08, nightLum);
    // Apply a gamma curve to make dim areas darker and bright areas pop
    vec3 cleanNight = nightColor * lightMask;
    vec3 litNight = cleanNight * uNightBrightness;

    vec3 finalColor = mix(litNight, litDay, blend);

    FragColor = vec4(finalColor, 1.0);
}
";

    public const string AtmosphereVertex = @"
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vWorldPos;
out vec3 vNormal;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    gl_Position = uProjection * uView * worldPos;
}
";

    public const string AtmosphereFragment = @"
#version 330 core

in vec3 vWorldPos;
in vec3 vNormal;

uniform vec3 uCameraPos;
uniform vec3 uSunDirection;

out vec4 FragColor;

void main()
{
    vec3 normal = normalize(vNormal);
    vec3 viewDir = normalize(uCameraPos - vWorldPos);
    vec3 sunDir = normalize(uSunDirection);

    float rim = 1.0 - max(dot(viewDir, normal), 0.0);
    rim = pow(rim, 3.0);

    float sunFacing = max(dot(normal, sunDir), 0.0) * 0.5 + 0.5;

    vec3 atmosColor = vec3(0.3, 0.5, 1.0) * rim * sunFacing * 0.6;

    FragColor = vec4(atmosColor, rim * 0.5);
}
";

    // ─── Flat Map shaders ───

    public const string FlatMapVertex = @"
#version 330 core

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

    public const string FlatMapFragment = @"
#version 330 core

in vec2 vTexCoord;

uniform sampler2D uDayTexture;
uniform sampler2D uNightTexture;
uniform vec3 uSunDirection;
uniform float uAmbient;
uniform float uNightBrightness;

out vec4 FragColor;

void main()
{
    // Convert tex coords to lat/lon
    float lon = (vTexCoord.x - 0.5) * 2.0 * 3.14159265;
    float lat = (vTexCoord.y - 0.5) * 3.14159265;

    // Compute surface normal on sphere for this lat/lon
    vec3 normal = vec3(
        cos(lat) * cos(lon),
        sin(lat),
        cos(lat) * sin(lon)
    );

    vec3 sunDir = normalize(uSunDirection);
    float NdotL = dot(normal, sunDir);
    float blend = smoothstep(-0.1, 0.1, NdotL);

    vec3 dayColor = texture(uDayTexture, vTexCoord).rgb;
    vec3 nightColor = texture(uNightTexture, vTexCoord).rgb;

    float dayLight = max(NdotL, 0.0) * 0.8 + uAmbient;
    vec3 litDay = dayColor * dayLight;

    // Night side: city lights with threshold to eliminate ocean glow
    float nightLum = dot(nightColor, vec3(0.299, 0.587, 0.114));
    float lightMask = smoothstep(0.02, 0.08, nightLum);
    vec3 cleanNight = nightColor * lightMask;
    vec3 litNight = cleanNight * uNightBrightness;

    vec3 finalColor = mix(litNight, litDay, blend);

    FragColor = vec4(finalColor, 1.0);
}
";

    // ─── Moon shaders ───

    public const string MoonVertex = @"
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vNormal;
out vec2 vTexCoord;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * worldPos;
}
";

    public const string MoonFragment = @"
#version 330 core

in vec3 vNormal;
in vec2 vTexCoord;

uniform sampler2D uMoonTexture;
uniform vec3 uSunDirection;
uniform float uAmbient;

out vec4 FragColor;

void main()
{
    vec3 normal = normalize(vNormal);
    vec3 sunDir = normalize(uSunDirection);

    float NdotL = dot(normal, sunDir);
    float light = max(NdotL, 0.0) * 0.9 + uAmbient;

    vec3 moonColor = texture(uMoonTexture, vTexCoord).rgb;
    vec3 finalColor = moonColor * light;

    FragColor = vec4(finalColor, 1.0);
}
";

    // ─── Still Image shaders (for EPIC satellite photos) ───

    public const string StillImageVertex = @"
#version 330 core

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

    public const string StillImageFragment = @"
#version 330 core

in vec2 vTexCoord;

uniform sampler2D uImage;
uniform float uImageAspect;   // image width / height
uniform float uScreenAspect;  // screen width / height

out vec4 FragColor;

void main()
{
    vec2 uv = vTexCoord;

    if (uImageAspect > uScreenAspect) {
        // Image is wider than screen: fit width, black bars top/bottom
        float scale = uScreenAspect / uImageAspect;
        uv.y = (uv.y - 0.5) / scale + 0.5;
    } else {
        // Image is taller than screen: fit height, black bars left/right
        float scale = uImageAspect / uScreenAspect;
        uv.x = (uv.x - 0.5) / scale + 0.5;
    }

    // Black bars for out-of-bounds UVs
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
        FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    } else {
        FragColor = vec4(texture(uImage, uv).rgb, 1.0);
    }
}
";

    // ─── Star background shaders ───

    public const string StarsVertex = @"
#version 330 core

layout(location = 0) in vec2 aPosition;

out vec2 vScreenPos;

void main()
{
    vScreenPos = aPosition;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

    public const string StarsFragment = @"
#version 330 core

in vec2 vScreenPos;
uniform vec3 uResolution;

out vec4 FragColor;

// Hash function for pseudo-random star placement
float hash(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main()
{
    // Scale screen coords to create a grid of cells
    vec2 uv = (vScreenPos * 0.5 + 0.5) * uResolution.xy;

    // Multiple star layers for depth
    vec3 color = vec3(0.0);

    // Layer 1: Dense dim stars (small)
    {
        float cellSize = 8.0;
        vec2 cell = floor(uv / cellSize);
        vec2 cellUV = fract(uv / cellSize);

        float starPresence = hash(cell);
        if (starPresence > 0.92)
        {
            vec2 starPos = vec2(hash(cell + 1.0), hash(cell + 2.0));
            float d = length(cellUV - starPos);
            float brightness = hash(cell + 3.0) * 0.4 + 0.1;
            float radius = 0.04 + hash(cell + 4.0) * 0.03;
            float star = smoothstep(radius, 0.0, d);
            color += vec3(brightness) * star;
        }
    }

    // Layer 2: Medium stars
    {
        float cellSize = 20.0;
        vec2 cell = floor(uv / cellSize);
        vec2 cellUV = fract(uv / cellSize);

        float starPresence = hash(cell + 100.0);
        if (starPresence > 0.90)
        {
            vec2 starPos = vec2(hash(cell + 101.0), hash(cell + 102.0));
            float d = length(cellUV - starPos);
            float brightness = hash(cell + 103.0) * 0.5 + 0.3;
            float radius = 0.03 + hash(cell + 104.0) * 0.04;
            float star = smoothstep(radius, 0.0, d);

            // Slight warm/cool color variation
            float tint = hash(cell + 105.0);
            vec3 starColor = mix(vec3(0.8, 0.85, 1.0), vec3(1.0, 0.95, 0.85), tint);
            color += starColor * brightness * star;
        }
    }

    // Layer 3: Bright sparse stars
    {
        float cellSize = 50.0;
        vec2 cell = floor(uv / cellSize);
        vec2 cellUV = fract(uv / cellSize);

        float starPresence = hash(cell + 200.0);
        if (starPresence > 0.85)
        {
            vec2 starPos = vec2(hash(cell + 201.0), hash(cell + 202.0));
            float d = length(cellUV - starPos);
            float brightness = hash(cell + 203.0) * 0.4 + 0.6;
            float radius = 0.02 + hash(cell + 204.0) * 0.03;
            float star = smoothstep(radius, 0.0, d);

            float tint = hash(cell + 205.0);
            vec3 starColor = mix(vec3(0.7, 0.8, 1.0), vec3(1.0, 0.9, 0.8), tint);
            color += starColor * brightness * star;
        }
    }

    FragColor = vec4(color, 1.0);
}
";
}
