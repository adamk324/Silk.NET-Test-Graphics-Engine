#version 330 core
out vec4 FragColor;

in vec3 Normal;
in vec3 FragPos;
in vec4 FragPosLightSpace;

uniform vec3 lightPos;
uniform vec3 viewPos;
uniform vec3 objectColor;
uniform bool isDepthPass;
uniform vec3 ambientStrength;
uniform vec3 diffuseStrength;
uniform vec3 specularStrength;
uniform sampler2D shadowMap;

float ShadowCalculation(vec4 fragPosLightSpace, vec3 normal, vec3 lightDir)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;

    // Out-of-bounds fragments cast no shadow
    if (projCoords.z > 1.0)
    {
        return 0.0;
    }

    float closestDepth = texture(shadowMap, projCoords.xy).r;
    float currentDepth = projCoords.z;

    // Bias to reduce shadow acne
    float bias = max(0.005 * (1.0 - dot(normal, lightDir)), 0.0005);

    // PCF - Percentage Closer Filtering for soft shadows
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    for (int x = -1; x <= 1; ++x)
    {
        for (int y = -1; y <= 1; ++y)
        {
            float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x, y) * texelSize).r;
            shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;
        }
    }
    shadow /= 9.0;

    return shadow;
}

void main()
{
    if (isDepthPass)
    {
        // Depth pass - just write depth, no color output needed
        return;
    }

    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(lightPos - FragPos);
    
    // Ambient
    vec3 ambient = ambientStrength * objectColor;

    // Diffuse
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diffuseStrength * diff * objectColor;

    // Specular
    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0);
    vec3 specular = specularStrength * spec * objectColor;

    // Shadow calculation
    float shadow = ShadowCalculation(FragPosLightSpace, norm, lightDir);

    // Combine lighting with shadow attenuation
    vec3 lighting = ambient + (1.0 - shadow * 0.8) * (diffuse + specular);

    FragColor = vec4(lighting, 1.0);
}