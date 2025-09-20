namespace PTfinder.API.DATA.DTO
{
    public class PartnerCreateDto
    {
        public string Name { get; set; }
        public string LogoUrl { get; set; }
        public string Description { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string PlanName { get; set; }
        public int MaxCoaches { get; set; }
        public decimal PricePerMonth { get; set; }
        public decimal PricePerYear { get; set; }
    }

    public class PartnerReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string LogoUrl { get; set; }
        public string PlanName { get; set; }
        public int MaxCoaches { get; set; }
        public bool IsActive { get; set; }
    }

}
