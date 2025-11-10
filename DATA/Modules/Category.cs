namespace PTfinder.API.DATA.Modules
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ICollection<Speciality> Specialities { get; set; }
        public ICollection<Coach> Coaches { get; set; }
    }
}
