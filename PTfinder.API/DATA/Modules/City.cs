namespace PTfinder.API.DATA.Modules
{
    public class City
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public int CountryId { get; set; }
        public Country Country { get; set; }

        public ICollection<Area> Areas { get; set; }
    }

}
