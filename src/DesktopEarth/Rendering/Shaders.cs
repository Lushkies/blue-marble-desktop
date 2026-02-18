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

out vec3 vWorldPos;
out vec3 vNormal;
out vec2 vTexCoord;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * worldPos;
}
";

    public const string EarthFragment = @"
#version 330 core

in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vTexCoord;

uniform sampler2D uDayTexture;
uniform sampler2D uNightTexture;
uniform vec3 uSunDirection;
uniform vec3 uCameraPos;
uniform float uAmbient;
uniform float uNightBrightness;
uniform float uSpecularIntensity;
uniform float uSpecularPower;
uniform sampler2D uBathyMask;
uniform bool uHasBathyMask;

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

    // Specular reflection on water (Blinn-Phong)
    if (uSpecularIntensity > 0.0 && uHasBathyMask)
    {
        vec3 viewDir = normalize(uCameraPos - vWorldPos);
        vec3 halfDir = normalize(sunDir + viewDir);
        float spec = pow(max(dot(normal, halfDir), 0.0), uSpecularPower);
        float waterMask = texture(uBathyMask, vTexCoord).r;
        litDay += vec3(1.0) * spec * uSpecularIntensity * waterMask * max(blend, 0.0);
    }

    vec3 litNight = nightColor * uNightBrightness;

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
    vec3 litNight = nightColor * uNightBrightness;

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
}
