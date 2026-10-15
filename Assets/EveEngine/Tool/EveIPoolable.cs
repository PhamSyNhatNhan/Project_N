namespace EveEngine
{
    public interface EveIPoolable
    {
        void OnPoolCreated();
        void OnPoolGet(EveEasyPooling pool);
        void OnPoolRelease();
    }
}
