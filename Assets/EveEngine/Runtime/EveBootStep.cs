namespace EveEngine
{
    // EN: One finished step of the staged content load (EveRuntime.ContentLoadSteps): its name and how much of the load is done (0..1, nominal
    //     weights; the real time of each step is measured by the caller).
    // VI: Một bước đã xong của việc nạp nội dung theo giai đoạn: tên bước và phần đã xong (0..1).
    public readonly struct EveBootStep
    {
        public readonly string Name;
        public readonly float Fraction;
        public EveBootStep(string name, float fraction) { Name = name; Fraction = fraction; }
    }
}
