#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;

out vec3 Normal;
out vec3 FragPos;
out vec4 FragPosLightSpace;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 lightSpaceMatrix;
uniform bool isDepthPass;

void main()
{
    vec4 worldPos = model * vec4(aPos, 1.0);
    FragPos = worldPos.xyz;
    FragPosLightSpace = lightSpaceMatrix * worldPos;
    
    if (!isDepthPass)
    {
        Normal = mat3(transpose(inverse(model))) * aNormal;
    }
    else
    {
        Normal = aNormal;
    }
    
    gl_Position = projection * view * worldPos;
}