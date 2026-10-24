using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.People;

public class PhotosTests
{
    private const int SchoolId = 1;

    private readonly MessageAttachmentStorageService storage = new(new FakeWebHostEnvironment());
    private readonly FakeEmailSender emails = new();

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private PhotoStorage Photos => new(storage);

    private PhotosController Controller(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)),
            Photos, emails, Options.Create(new AppLinkOptions()), new Notifier(db, TimeProvider.System),
            NullLogger<PhotosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role.ToString())])),
                },
            },
        };

    private sealed record Fixture(
        School School, User Admin, User ClassTeacher, User OtherTeacher, User Parent, User OtherParent, User Pupil, Student Child);

    private static async Task<Fixture> SeedAsync(AppDbContext db, bool photosOn = true)
    {
        var school = new School { Id = SchoolId, Name = "Škola", Slug = "skola", StudentPhotos = photosOn, ParentPhotos = photosOn };
        db.Schools.Add(school);
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        var classTeacher = new User { Username = "tridni", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        var otherTeacher = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        var otherParent = new User { Username = "rodic2", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        var pupil = new User { Username = "zak", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.AddRange(admin, classTeacher, otherTeacher, parent, otherParent, pupil);
        await db.SaveChangesAsync();
        db.Persons.Add(new Person { UserId = parent.Id, SchoolId = SchoolId, FirstName = "Petr", LastName = "Rodič", Email = "petr@example.com" });
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = classTeacher.Id };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var child = new Student { FirstName = "Jan", LastName = "Rodič", SchoolClassId = schoolClass.Id, SchoolId = SchoolId, UserId = pupil.Id };
        db.Students.Add(child);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.SaveChangesAsync();
        return new Fixture(school, admin, classTeacher, otherTeacher, parent, otherParent, pupil, child);
    }

    private static async Task ShareAsync(AppDbContext db, VisibilityOwner owner, VisibilityAudience audience, VisibilityScope scope)
    {
        db.VisibilityRules.Add(new VisibilityRule
        {
            SchoolId = SchoolId, Owner = owner, Field = VisibilityField.Photo, Audience = audience,
            Min = VisibilityScope.Nobody, Default = scope, Max = scope,
        });
        if (owner != VisibilityOwner.Staff)
        {
            db.VisibilityRules.Add(new VisibilityRule
            {
                SchoolId = SchoolId, Owner = owner, Field = VisibilityField.Name, Audience = audience,
                Min = VisibilityScope.Nobody, Default = VisibilityScope.School, Max = VisibilityScope.School,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Gives <paramref name="parent"/> a child in the fixture's class.</summary>
    private static async Task AddChildInClassAsync(AppDbContext db, Fixture fx, User parent)
    {
        var child = new Student { FirstName = "Eva", LastName = "Jiná", SchoolClassId = fx.Child.SchoolClassId, SchoolId = SchoolId };
        db.Students.Add(child);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.SaveChangesAsync();
    }

    /// <summary>400×200 JPEG, left half red, right half blue — optionally
    /// with an EXIF orientation tag (6 = rotate 90° clockwise to view).</summary>
    private static byte[] TestJpeg(ushort? orientation = null)
    {
        using var bitmap = new SKBitmap(400, 200);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawRect(0, 0, 200, 200, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(200, 0, 200, 200, new SKPaint { Color = SKColors.Blue });
        }
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
        if (orientation is not { } o)
        {
            return jpeg;
        }
        // APP1 "Exif": big-endian TIFF, one IFD entry (0x0112 Orientation, SHORT).
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,
            0, 1,
            0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(o >> 8), (byte)o, 0, 0,
            0, 0, 0, 0,
        ];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static IFormFile File(byte[] content, string name = "fotka.jpg") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", name);

    [Fact]
    public async Task UploadIsTurnedUprightCroppedAndStrippedOfExif()
    {
        var key = await Photos.SaveAsync(SchoolId, File(TestJpeg(orientation: 6)));

        Assert.NotNull(key);
        var thumbBytes = await System.IO.File.ReadAllBytesAsync(Photos.FullPath(key, PhotoSize.Thumb));
        using var thumb = SKBitmap.Decode(thumbBytes);
        Assert.Equal(128, thumb.Width);
        Assert.Equal(128, thumb.Height);
        // Rotated clockwise, the red left half is on top.
        Assert.True(thumb.GetPixel(64, 10).Red > 200 && thumb.GetPixel(64, 10).Blue < 80);
        Assert.True(thumb.GetPixel(64, 118).Blue > 200 && thumb.GetPixel(64, 118).Red < 80);
        Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(thumbBytes));
        using var profile = SKBitmap.Decode(Photos.FullPath(key, PhotoSize.Profile));
        Assert.Equal(200, profile.Width); // never upscaled
    }

    [Theory]
    [InlineData("dokument.pdf")]
    [InlineData("fotka.gif")]
    public void OnlyImagesAreAccepted(string name) =>
        Assert.NotNull(PhotoStorage.Validate(File([1, 2, 3], name)));

    [Fact]
    public async Task NonImageWithImageNameIsRefused()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var result = await Controller(db, fx.ClassTeacher).UploadUserPhoto(fx.ClassTeacher.Id, File([1, 2, 3, 4]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task StaffPhotoReachesParentsOnlyWhenShared()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        Assert.IsType<OkObjectResult>((await Controller(db, fx.ClassTeacher).UploadUserPhoto(fx.ClassTeacher.Id, File(TestJpeg()))).Result);

        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.OtherTeacher).GetUserPhoto(fx.ClassTeacher.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.Parent).GetUserPhoto(fx.ClassTeacher.Id, PhotoSize.Thumb));

        await ShareAsync(db, VisibilityOwner.Staff, VisibilityAudience.Parents, VisibilityScope.MyClasses);
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.Parent).GetUserPhoto(fx.ClassTeacher.Id, PhotoSize.Thumb));
        // The other teacher doesn't teach the child's class.
        Assert.IsType<OkObjectResult>((await Controller(db, fx.OtherTeacher).UploadUserPhoto(fx.OtherTeacher.Id, File(TestJpeg()))).Result);
        Assert.IsType<NotFoundResult>(await Controller(db, fx.Parent).GetUserPhoto(fx.OtherTeacher.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.Pupil).GetUserPhoto(fx.ClassTeacher.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task ParentPhotoNeedsTheSchoolOptionAndReachesOnlyTheirChildsTeachers()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, photosOn: false);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, fx.Parent).UploadUserPhoto(fx.Parent.Id, File(TestJpeg()))).Result);

        fx.School.ParentPhotos = true;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await Controller(db, fx.Parent).UploadUserPhoto(fx.Parent.Id, File(TestJpeg()))).Result);

        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.ClassTeacher).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.Admin).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherTeacher).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherParent).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task NobodyMaySetSomeoneElsesPhotoExceptAdminForStaff()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        Assert.IsType<ForbidResult>((await Controller(db, fx.OtherTeacher).UploadUserPhoto(fx.ClassTeacher.Id, File(TestJpeg()))).Result);
        Assert.IsType<ForbidResult>((await Controller(db, fx.Admin).UploadUserPhoto(fx.Parent.Id, File(TestJpeg()))).Result);
        Assert.IsType<OkObjectResult>((await Controller(db, fx.Admin).UploadUserPhoto(fx.ClassTeacher.Id, File(TestJpeg()))).Result);
    }

    [Fact]
    public async Task PupilPhotoNeedsConsentEvenForTheSchool()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        Assert.IsType<BadRequestObjectResult>((await Controller(db, fx.Admin).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()))).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, fx.Pupil).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()))).Result);
    }

    [Fact]
    public async Task PupilsOwnUploadWaitsForApprovalAndNotifiesParents()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.Parent).GiveConsent(fx.Child.Id, new PhotoConsentRequest(null));
        Assert.Equal("Petr Rodič", fx.Child.PhotoConsentGivenBy);

        var upload = await Controller(db, fx.Pupil).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));

        var state = Assert.IsType<StudentPhotoStateDto>(Assert.IsType<OkObjectResult>(upload.Result).Value);
        Assert.Equal("Pending", state.Status);
        Assert.Equal("petr@example.com", Assert.Single(emails.Sent).To);
        // Pending: the pupil and approvers only.
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.Pupil).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.ClassTeacher).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherTeacher).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
        Assert.IsType<ForbidResult>((await Controller(db, fx.Pupil).ApproveStudentPhoto(fx.Child.Id)).Result);

        Assert.IsType<OkObjectResult>((await Controller(db, fx.Parent).ApproveStudentPhoto(fx.Child.Id)).Result);
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.OtherTeacher).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherParent).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task ParentsUploadIsApprovedRightAwayButOnlyForTheirOwnChild()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, fx.Parent).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()))).Result);
        await Controller(db, fx.Parent).GiveConsent(fx.Child.Id, new PhotoConsentRequest(null));

        Assert.IsType<ForbidResult>((await Controller(db, fx.OtherParent).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()))).Result);
        var upload = await Controller(db, fx.Parent).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));

        var state = Assert.IsType<StudentPhotoStateDto>(Assert.IsType<OkObjectResult>(upload.Result).Value);
        Assert.Equal("Approved", state.Status);
        Assert.True(state.CanUpload);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task RejectingAndRevokingConsentDeleteTheFiles()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.ClassTeacher).GiveConsent(fx.Child.Id, new PhotoConsentRequest("Petra Rodičová"));
        Assert.Equal("Petra Rodičová", fx.Child.PhotoConsentGivenBy);

        await Controller(db, fx.Pupil).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));
        var pendingPath = Photos.FullPath(fx.Child.PhotoKey!, PhotoSize.Profile);
        await Controller(db, fx.Parent).RejectStudentPhoto(fx.Child.Id);
        Assert.Null(fx.Child.PhotoKey);
        Assert.False(System.IO.File.Exists(pendingPath));

        var upload = await Controller(db, fx.ClassTeacher).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));
        Assert.Equal("Approved", Assert.IsType<StudentPhotoStateDto>(Assert.IsType<OkObjectResult>(upload.Result).Value).Status);
        var approvedPath = Photos.FullPath(fx.Child.PhotoKey!, PhotoSize.Thumb);
        await Controller(db, fx.Parent).RevokeConsent(fx.Child.Id);
        Assert.Null(fx.Child.PhotoKey);
        Assert.Null(fx.Child.PhotoConsentAt);
        Assert.False(System.IO.File.Exists(approvedPath));
    }

    [Fact]
    public async Task SchoolMustNameTheGuardianAndStrangersCannotConsent()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        Assert.IsType<BadRequestObjectResult>((await Controller(db, fx.Admin).GiveConsent(fx.Child.Id, new PhotoConsentRequest(" "))).Result);
        Assert.IsType<ForbidResult>((await Controller(db, fx.OtherParent).GiveConsent(fx.Child.Id, new PhotoConsentRequest("X"))).Result);
        Assert.IsType<ForbidResult>((await Controller(db, fx.OtherTeacher).GiveConsent(fx.Child.Id, new PhotoConsentRequest("X"))).Result);
        Assert.IsType<ForbidResult>((await Controller(db, fx.Pupil).GiveConsent(fx.Child.Id, new PhotoConsentRequest("X"))).Result);
    }

    [Fact]
    public async Task PupilPhotosOffHidesThemFromEveryone()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.Parent).GiveConsent(fx.Child.Id, new PhotoConsentRequest(null));
        await Controller(db, fx.Admin).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));

        fx.School.StudentPhotos = false;
        await db.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await Controller(db, fx.Admin).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.Parent).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task OtherParentsSeeAParentsPhotoOnlyWhereShared()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.Parent).UploadUserPhoto(fx.Parent.Id, File(TestJpeg()));
        await AddChildInClassAsync(db, fx, fx.OtherParent);

        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherParent).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));

        await ShareAsync(db, VisibilityOwner.Parent, VisibilityAudience.Parents, VisibilityScope.MyClasses);
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.OtherParent).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
        Assert.IsType<NotFoundResult>(await Controller(db, fx.Pupil).GetUserPhoto(fx.Parent.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task ApprovedPupilPhotoReachesTheClassOnlyWhereShared()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.Parent).GiveConsent(fx.Child.Id, new PhotoConsentRequest(null));
        await Controller(db, fx.Pupil).UploadStudentPhoto(fx.Child.Id, File(TestJpeg()));
        await AddChildInClassAsync(db, fx, fx.OtherParent);
        await ShareAsync(db, VisibilityOwner.Student, VisibilityAudience.Parents, VisibilityScope.MyClasses);

        // Pending: still only the pupil and approvers.
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherParent).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));

        await Controller(db, fx.Parent).ApproveStudentPhoto(fx.Child.Id);
        Assert.IsType<PhysicalFileResult>(await Controller(db, fx.OtherParent).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));

        // A parent decides for the child: back to nobody.
        db.VisibilityChoices.Add(new VisibilityChoice
        {
            SchoolId = SchoolId, StudentId = fx.Child.Id, Owner = VisibilityOwner.Student, Field = VisibilityField.Photo,
            Audience = VisibilityAudience.Parents, Scope = VisibilityScope.Nobody,
        });
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Controller(db, fx.OtherParent).GetStudentPhoto(fx.Child.Id, PhotoSize.Thumb));
    }

    [Fact]
    public async Task ParentListCarriesThePhotoOnlyForThoseWhoMaySeeIt()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await Controller(db, fx.Parent).UploadUserPhoto(fx.Parent.Id, File(TestJpeg()));

        async Task<string?> PhotoSeenBy(User viewer)
        {
            var controller = new ParentsController(db, new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)))
            {
                ControllerContext = Controller(db, viewer).ControllerContext,
            };
            var parents = Assert.IsAssignableFrom<IEnumerable<ParentDto>>(
                Assert.IsType<OkObjectResult>((await controller.GetParents()).Result).Value);
            return parents.Single(p => p.Id == fx.Parent.Id).Photo;
        }

        Assert.NotNull(await PhotoSeenBy(fx.Admin));
        Assert.NotNull(await PhotoSeenBy(fx.ClassTeacher));
        Assert.Null(await PhotoSeenBy(fx.OtherTeacher));
    }
}
