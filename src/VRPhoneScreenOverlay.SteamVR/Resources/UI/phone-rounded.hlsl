cbuffer Geometry : register(b0) { float2 size; float radius; float padding; };
float4 Vertex(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}
float4 Pixel(float4 position : SV_Position) : SV_Target
{
    float2 q = abs(position.xy - size * 0.5) - (size * 0.5 - radius);
    float distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
    return float4(0, 0, 0, saturate(0.5 - distance));
}
