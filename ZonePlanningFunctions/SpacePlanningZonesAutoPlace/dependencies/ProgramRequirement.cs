namespace Elements
{
    public partial class ProgramRequirement
    {
        public int CountPlaced { get; set; }
        public int RemainingToPlace
        {
            get
            {
                return this.SpaceCount - this.CountPlaced;
            }
        }
    }
}