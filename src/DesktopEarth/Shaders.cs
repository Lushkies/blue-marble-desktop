namespace DesktopEarth;

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
uniform float uAmbient;

out vec4 FragColor;

void main()
{
    vec3 normal = normalize(vNormal);
    vec3 sunDir = normalize(uSunDirection);

    // Diffuse lighting (how much this point faces the sun)
    float NdotL = dot(normal, sunDir);

    // Smooth transition between day and night
    // The transition zone is roughly +/- 0.1 around the terminator
    float blend = smoothstep(-0.1, 0.1, NdotL);

    vec3 dayColor = texture(uDayTexture, vTexCoord).rgb;
    vec3 nightColor = texture(uNightTexture, vTexCoord).rgb;

    // On the day side, apply diffuse lighting
    float dayLight = max(NdotL, 0.0) * 0.8 + uAmbient;
    vec3 litDay = dayColor * dayLight;

    // Night side shows city lights
    vec3 litNight = nightColor * 1.2;

    // Blend between day and night
    vec3 finalColor = mix(litNight, litDay, blend);

    FragColor = vec4(finalColor, 1.0);
}
";

    // Simple atmosphere glow shader
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

    // Fresnel-like effect: glow stronger at edges
    float rim = 1.0 - max(dot(viewDir, normal), 0.0);
    rim = pow(rim, 3.0);

    // Sun-facing side gets more glow
    float sunFacing = max(dot(normal, sunDir), 0.0) * 0.5 + 0.5;

    // Atmosphere color (blue tint)
    vec3 atmosColor = vec3(0.3, 0.5, 1.0) * rim * sunFacing * 0.6;

    FragColor = vec4(atmosColor, rim * 0.5);
}
";
}
