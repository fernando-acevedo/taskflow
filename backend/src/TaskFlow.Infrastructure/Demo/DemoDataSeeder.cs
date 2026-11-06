using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Demo;

/// <summary>
/// The agency dataset of the public demo: clients, projects with members and labels, tasks across every status
/// with due dates relative to the reset day, comments and time entries. Built through the domain methods so it
/// obeys the same rules as the API.
/// </summary>
internal static class DemoDataSeeder
{
    /// <summary>Fictional colleagues (display name, email) that nobody can sign in as.</summary>
    public static readonly (string Name, string Email)[] Teammates =
    [
        ("Marta Ruiz", "marta.ruiz@demo.taskflow.invalid"),
        ("Diego Fernández", "diego.fernandez@demo.taskflow.invalid"),
    ];

    private static readonly (string Name, string Email, string Notes)[] ClientData =
    [
        ("Northwind Coffee", "hello@northwind-coffee.example", "Regional roaster, 12 shops. Monthly retainer."),
        ("Lumen Dental", "office@lumen-dental.example", "Clinic network launching online booking."),
        ("Orbit Fintech", "product@orbit-fintech.example", "Series A startup, fast-moving scope."),
        ("Greenleaf Studio", "studio@greenleaf.example", "Interior design studio, brand refresh."),
    ];

    private static readonly (int Client, string Name, string Description)[] ProjectData =
    [
        (0, "Website redesign", "New storefront and store locator for Northwind."),
        (1, "Online booking app", "Patients book and reschedule appointments."),
        (2, "Customer onboarding flow", "KYC and first-deposit journey."),
        (3, "Brand refresh", "Logo, palette and a new portfolio site."),
    ];

    private static readonly (string Name, string Color)[] LabelData =
    [
        ("Design", "#8e24aa"),
        ("Development", "#1e88e5"),
        ("Content", "#43a047"),
        ("Bug", "#e53935"),
    ];

    // Project, title, status, priority, assignee (0 admin, 1 member, 2-3 teammates), due offset in days, estimate
    // in minutes, labels, minutes already logged.
    private static readonly SeedTask[] Tasks =
    [
        new(0, "Audit current site and analytics", TaskItemStatus.Done, TaskPriority.Medium, 1, -12, 240, [1], 270),
        new(0, "Homepage wireframes", TaskItemStatus.Done, TaskPriority.High, 2, -8, 300, [0], 330),
        new(0, "Visual design: homepage", TaskItemStatus.Review, TaskPriority.High, 2, 1, 480, [0], 420),
        new(0, "Build store locator", TaskItemStatus.InProgress, TaskPriority.High, 1, 4, 600, [1], 240),
        new(0, "Write menu and product copy", TaskItemStatus.InProgress, TaskPriority.Medium, 3, 6, 360, [2], 120),
        new(0, "Fix cart total on mobile", TaskItemStatus.ToDo, TaskPriority.Critical, 1, -2, 90, [1, 3], 0),
        new(0, "Set up redirects from the old site", TaskItemStatus.ToDo, TaskPriority.Low, 0, 12, 120, [1], 0),
        new(1, "Define booking rules with the clinic", TaskItemStatus.Done, TaskPriority.High, 0, -10, 180, [2], 200),
        new(1, "Appointment calendar UI", TaskItemStatus.InProgress, TaskPriority.High, 1, 3, 540, [0, 1], 300),
        new(1, "Booking API and double-booking guard", TaskItemStatus.InProgress, TaskPriority.Critical, 2, 2, 720, [1], 360),
        new(1, "SMS reminders", TaskItemStatus.ToDo, TaskPriority.Medium, 1, 10, 300, [1], 0),
        new(1, "Rescheduling emails: copy review", TaskItemStatus.Review, TaskPriority.Low, 3, 0, 90, [2], 60),
        new(1, "Accessibility pass on the form", TaskItemStatus.ToDo, TaskPriority.Medium, null, 14, 240, [0], 0),
        new(2, "Map the KYC steps", TaskItemStatus.Done, TaskPriority.High, 3, -14, 240, [2], 250),
        new(2, "Onboarding screens: first draft", TaskItemStatus.Done, TaskPriority.Medium, 2, -6, 420, [0], 400),
        new(2, "Document upload component", TaskItemStatus.InProgress, TaskPriority.High, 1, 5, 480, [1], 180),
        new(2, "Error states for failed verification", TaskItemStatus.ToDo, TaskPriority.High, 0, 7, 180, [0, 1], 0),
        new(2, "First deposit confirmation bug", TaskItemStatus.Review, TaskPriority.Critical, 2, -1, 120, [3, 1], 150),
        new(2, "Usability test with five users", TaskItemStatus.ToDo, TaskPriority.Medium, 0, 15, 360, [0], 0),
        new(3, "Moodboard and references", TaskItemStatus.Done, TaskPriority.Low, 3, -20, 120, [0], 110),
        new(3, "Logo concepts", TaskItemStatus.Done, TaskPriority.High, 2, -9, 600, [0], 640),
        new(3, "Color palette and typography", TaskItemStatus.Review, TaskPriority.Medium, 2, 2, 240, [0], 200),
        new(3, "Portfolio site: project pages", TaskItemStatus.InProgress, TaskPriority.Medium, 1, 8, 480, [1, 2], 90),
        new(3, "Photograph the showroom", TaskItemStatus.ToDo, TaskPriority.Medium, null, 9, 300, [2], 0),
        new(3, "Brand guidelines PDF", TaskItemStatus.ToDo, TaskPriority.Low, 0, 20, 360, [0, 2], 0),
        new(3, "Social media templates", TaskItemStatus.ToDo, TaskPriority.Low, 3, 18, 240, [0], 0),
        new(2, "Rate limit the sign-up endpoint", TaskItemStatus.InProgress, TaskPriority.Medium, 1, 3, 150, [1], 45),
        new(0, "Newsletter signup banner", TaskItemStatus.ToDo, TaskPriority.Low, null, null, null, [0, 2], 0),
    ];

    private static readonly (int Task, int Author, string Body)[] Comments =
    [
        (2, 0, "Looks great. Can we try a warmer hero image before the client review?"),
        (3, 2, "The locator needs opening hours per shop. Marta, do we have them in the spreadsheet?"),
        (5, 1, "Reproduced on iOS Safari only. The tax line rounds twice."),
        (9, 0, "Please add a test for two patients picking the same slot at the same time."),
        (17, 1, "Fixed in the staging build, waiting for QA on the deposit screen."),
        (21, 0, "Client approved the palette. Moving this to Done once the contrast check passes."),
        (22, 2, "Draft pages are up on staging. Feedback welcome."),
    ];

    private static readonly string[] TimeNotes = ["Planning", "Implementation", "Review with the client", "Fixes after feedback", "Pairing"];

    private sealed record SeedTask(
        int Project,
        string Title,
        TaskItemStatus Status,
        TaskPriority Priority,
        int? Assignee,
        int? DueOffset,
        int? Estimate,
        int[] Labels,
        int LoggedMinutes);

    public static void Seed(AppDbContext db, IReadOnlyList<DemoUser> accounts, IReadOnlyList<Guid> teammates, DateTimeOffset now)
    {
        var admin = accounts.First(account => account.IsAdmin).Id;
        var member = accounts.FirstOrDefault(account => !account.IsAdmin)?.Id ?? admin;
        var extraAccounts = accounts.Select(account => account.Id).Where(id => id != admin && id != member).ToList();
        var people = new[] { admin, member, teammates[0], teammates[1] };
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var clients = ClientData.Select(data => Client.Create(data.Name, data.Email, data.Notes, now)).ToList();
        db.Clients.AddRange(clients);

        var projects = new List<Project>();
        var labels = new List<List<Label>>();
        foreach (var (clientIndex, name, description) in ProjectData)
        {
            var project = Unwrap(Project.Create(clients[clientIndex], name, description, admin, now));
            foreach (var userId in people.Skip(1).Concat(extraAccounts).Distinct())
            {
                Unwrap(project.AddMember(userId, ProjectRole.Member, now));
            }

            var projectLabels = new List<Label>();
            foreach (var (labelName, color) in LabelData)
            {
                projectLabels.Add(Unwrap(Label.Create(project, labelName, color, projectLabels, now)));
            }

            projects.Add(project);
            labels.Add(projectLabels);
        }

        db.Projects.AddRange(projects);
        db.Labels.AddRange(labels.SelectMany(projectLabels => projectLabels));

        var positions = new Dictionary<(int, TaskItemStatus), int>();
        var tasks = new List<TaskItem>();
        foreach (var seed in Tasks)
        {
            var project = projects[seed.Project];
            var assignee = seed.Assignee is { } index ? people[index] : (Guid?)null;
            var due = seed.DueOffset is { } offset ? today.AddDays(offset) : (DateOnly?)null;
            var task = Unwrap(TaskItem.Create(project, seed.Title, null, seed.Priority, assignee, due, admin, 0, now.AddDays(-14), seed.Estimate));

            var key = (seed.Project, seed.Status);
            var position = positions.GetValueOrDefault(key);
            positions[key] = position + 1;
            Unwrap(task.Move(project, seed.Status, position, now.AddDays(seed.Status == TaskItemStatus.Done ? -2 : -1)));
            Unwrap(task.SetLabels(project, [.. seed.Labels.Select(label => labels[seed.Project][label])]));
            tasks.Add(task);
        }

        db.Tasks.AddRange(tasks);

        foreach (var (taskIndex, authorIndex, body) in Comments)
        {
            var task = tasks[taskIndex];
            db.TaskComments.Add(Unwrap(TaskComment.Create(projects[Tasks[taskIndex].Project], task.Id, people[authorIndex], body, null, now.AddHours(-3 * (taskIndex + 1)))));
        }

        for (var i = 0; i < Tasks.Length; i++)
        {
            var seed = Tasks[i];
            if (seed.LoggedMinutes <= 0 || seed.Assignee is not { } assigneeIndex)
            {
                continue;
            }

            // Split the logged time over a few recent days (never in the future), so charts have a trend.
            var remaining = seed.LoggedMinutes;
            for (var part = 0; part < 3 && remaining > 0; part++)
            {
                var minutes = part == 2 ? remaining : Math.Min(remaining, Math.Max(15, seed.LoggedMinutes / 3 / 15 * 15));
                var date = today.AddDays(-(i % 5) - part);
                db.TimeEntries.Add(Unwrap(TimeEntry.Create(
                    projects[seed.Project], tasks[i].Id, people[assigneeIndex], date, minutes, TimeNotes[(i + part) % TimeNotes.Length], today, now)));
                remaining -= minutes;
            }
        }
    }

    private static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Demo seed is invalid: {result.Error.Description}");

    private static void Unwrap(Result result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Demo seed is invalid: {result.Error.Description}");
        }
    }
}
