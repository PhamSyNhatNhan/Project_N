/// <summary>
/// Effect implement interface này để nhận custom config data từ caller.
/// Manager chỉ biết interface, không biết kiểu data cụ thể.
/// </summary>
public interface IEffectConfigurable
{
    void Configure(object data);
}