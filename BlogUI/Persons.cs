namespace BlogUI
{
    public class Person
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string FullName => $"{FirstName} {LastName}";

        public string Image { get; set; }
    }

    public static class Persons
    {
        public static Person Avatar1 { get; set; } = new()
        {
            FirstName = "Daniel",
            LastName = "Mccoy",
            Image = "img/avatars/avatar.jpg",
        };

        public static Person Avatar2 { get; set; } = new()
        {
            FirstName = "Dale",
            LastName = "Summers",
            Image = "img/avatars/avatar-2.jpg",
        };

        public static Person Avatar3 { get; set; } = new()
        {
            FirstName = "Mary",
            LastName = "Fletcher",
            Image = "img/avatars/avatar-3.jpg",
        };

        public static Person Avatar4 { get; set; } = new()
        {
            FirstName = "Anne",
            LastName = "Cameron",
            Image = "img/avatars/avatar-4.jpg",
        };
    }
}
