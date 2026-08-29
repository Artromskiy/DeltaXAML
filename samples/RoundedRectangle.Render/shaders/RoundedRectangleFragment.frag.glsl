#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 16) vec4 member_Rect;
    layout(offset = 32) vec4 member_FillColor;
    layout(offset = 48) vec4 member_BorderColor;
    layout(offset = 64) vec4 member_CornerRadii;
    layout(offset = 80) float member_BorderWidth;
} pushConstants;

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 size = vec2(pushConstants.member_Rect.z, pushConstants.member_Rect.w);

    vec2 pixel = Uv* size;

    vec2 halfSize = size * 0.5;

    vec2 centered = pixel - halfSize;

    float radius = pushConstants.member_CornerRadii.x;

            if (centered.x> 0)
            {
                if (centered.y> 0)
                {
                    radius = pushConstants.member_CornerRadii.z;

                }
                else
                {
                    radius = pushConstants.member_CornerRadii.y;

                }
            }
            else if (centered.y> 0)
            {
                radius = pushConstants.member_CornerRadii.w;

            }
    vec2 q = abs(centered)- halfSize + vec2(radius, radius);

    vec2 outside = max(q, 0);

    float outsideDistance = length(outside);

    float insideDistance = min(max(q.x, q.y), 0);

    float distance = outsideDistance + insideDistance - radius;

    float edge = fwidth(distance);

    float fillCoverage = 1 - smoothstep(-edge, edge, distance);

    float innerCoverage = 1 - smoothstep(-edge, edge, distance + pushConstants.member_BorderWidth);

    float borderCoverage = max(fillCoverage - innerCoverage, 0);

    {fragColor = pushConstants.member_FillColor* innerCoverage +
    pushConstants.member_BorderColor* borderCoverage;
    return;
    }

}
