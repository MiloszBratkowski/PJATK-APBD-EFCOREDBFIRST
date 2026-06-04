namespace PJATK_APBD_EFCOREDBFIRST.DTOs;

public class AssignBedDto
{
    public DateTime From { get; set; }
    public DateTime? To { get; set; }
    public string BedType { get; set; } = null!;
    public string Ward { get; set; } = null!;
}