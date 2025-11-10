namespace PTfinder.API.DATA.DTO
{
    public class CoachUpdateDto
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string Gender { get; set; }

        public decimal Price { get; set; }
        public string Description { get; set; }

        public int CountryId { get; set; }
        public int CityId { get; set; }
        public int AreaId { get; set; }

        public int CategoryId { get; set; }
        public int SpecialityId { get; set; }

        public IFormFile ProfileImage { get; set; } 
    }

}
