using System.Diagnostics.Metrics;

namespace PTfinder.API.DATA.Modules
{
    public class Coach
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string Gender { get; set; }

        public decimal Price { get; set; }

        public string Description { get; set; }

        public int CountryId { get; set; }
        public Country Country { get; set; }

        public int CityId { get; set; }
        public City City { get; set; }

        public int AreaId { get; set; }
        public Area Area { get; set; }

        public int CategoryId { get; set; }
        public Category Category { get; set; }

        public int SpecialityId { get; set; }
        public Speciality Speciality { get; set; }

        public string ProfileImage { get; set; }

        public List<Availability> Availabilities { get; set; }
        public List<Booking> Bookings { get; set; }
        public ICollection<Review> Reviews { get; set; }
        public ICollection<Subscription> Subscriptions { get; set; }
        public ICollection<GalleryMedia> GalleryMedia { get; set; }

    }

}
