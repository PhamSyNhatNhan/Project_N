public interface ILoadable<T>
{
    void LoadRawData(T data);
    void ApplyData();
}