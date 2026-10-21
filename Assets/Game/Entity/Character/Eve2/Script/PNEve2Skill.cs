using UnityEngine;

// EN: Eve 2's own skill class. It derives from the ordinary player skill (not from Eve's moveset), so for now Eve 2 has no skills of its own and only the
//     default shared Dash binding (PNPlayerSkill falls back to `project_n:binding_player_dash`). Its skills get written here when they exist.
// VI: Class skill riêng của Eve 2: kế thừa skill người chơi thường (không phải bộ chiêu Eve) nên hiện chưa có skill, chỉ có Dash mặc định dùng chung.
[DisallowMultipleComponent]
public class PNEve2Skill : PNPlayerSkill
{
}
