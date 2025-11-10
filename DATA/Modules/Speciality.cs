namespace PTfinder.API.DATA.Modules
{
    public class Speciality
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int CategoryId { get; set; }
        public Category Category { get; set; }
        public ICollection<Coach> Coaches { get; set; }
    }
}
