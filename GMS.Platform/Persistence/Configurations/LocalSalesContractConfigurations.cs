namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class LocalSalesContractTermsConfiguration : IEntityTypeConfiguration<LocalSalesContractTerms>
{
    public void Configure(EntityTypeBuilder<LocalSalesContractTerms> builder)
    {
        builder.ToTable("local_sales_contract_terms", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TermsEn).IsRequired();
        builder.Property(x => x.TermsAr).IsRequired();
    }
}

public class LocalSalesContractDocumentConfiguration : IEntityTypeConfiguration<LocalSalesContractDocument>
{
    public void Configure(EntityTypeBuilder<LocalSalesContractDocument> builder)
    {
        builder.ToTable("local_sales_contract_documents", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_local_sales_contract_documents_language",
                "[Language] IN ('en','ar')");
            t.HasCheckConstraint(
                "CK_local_sales_contract_documents_status",
                "[Status] IN ('issued')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContractNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SnapshotJson).IsRequired();

        builder.HasIndex(x => x.ContractId).IsUnique();
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.ContractNumber);

        builder.HasOne(x => x.Contract)
            .WithMany()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
