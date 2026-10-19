using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Clients;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.Property(client => client.Id).ValueGeneratedNever();
        builder.Property(client => client.Name).HasMaxLength(Client.NameMaxLength);
        builder.Property(client => client.ContactEmail).HasMaxLength(Client.ContactEmailMaxLength);
        builder.Property(client => client.Notes).HasMaxLength(Client.NotesMaxLength);
        builder.HasIndex(client => client.Name).IsUnique();
    }
}
