using AutoMapper;

namespace api.Utils;

public class MappingProfile : Profile
{
	public MappingProfile()
	{
		CreateMap<User, UserDto>().MaxDepth(1);
	}
}