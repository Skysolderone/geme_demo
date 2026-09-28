# 多批次对照表（Q1）：python table.py <批次目录>...   先对每个目录跑 summarize.py 生成 engagement-summary.json
import json, sys, os
sys.stdout.reconfigure(encoding='utf-8')
cols = ['批次', '终局', '截断', '平均结束R', '无提子局', '总提子', '首提子R', 'R3领先胜', 'Pass率(R4+)', '终局棋子%', '终局禁入%', '棋子+禁入%',
        '首次出区R', '首次接触R', '落进对方区']
print('| ' + ' | '.join(cols) + ' |')
print('|' + '---|' * len(cols))
for d in sys.argv[1:]:
    o = json.load(open(os.path.join(d, 'engagement-summary.json'), encoding='utf-8'))
    eb = o.get('end_board', {})
    row = [os.path.basename(d.rstrip('/\\')), ','.join(f'{k}×{v}' for k, v in o['reasons'].items()), o['truncated'], o['end_round_mean'],
           f"{o['no_capture_games']}/{o['n']}", o['captured_stones_total'], o['first_capture_round_mean'],
           f"{o['round3_leader_win'][0]}/{o['round3_leader_win'][1]}", f"{o['pass_rate_post'][2]}%",
           eb.get('stone_density'), eb.get('protected_share'), eb.get('stones_plus_protected'),
           o.get('first_outside_own_zone', {}).get('mean'), o.get('first_contact', {}).get('mean'),
           f"{o['n'] - o.get('first_into_opp_zone', {}).get('never', o['n'])}/{o['n']}局"]
    print('| ' + ' | '.join(str(x) for x in row) + ' |')
print()
cols2 = ['批次', '阶段', '决策', 'Pass率', '每决策落子', '部署上限', '落点 own/neutral/opp', '合法空格', '禁入(对我)', '硬约束淘汰%', '单点>阈值%',
         '无点>阈值决策', '贪心:保留/阈值撤回(其中正收益)/活形撤回', '敌方非活棋子均值', '有提子点的决策']
print('| ' + ' | '.join(cols2) + ' |')
print('|' + '---|' * len(cols2))
for d in sys.argv[1:]:
    o = json.load(open(os.path.join(d, 'engagement-summary.json'), encoding='utf-8'))
    for b, r in o.get('by_phase', {}).items():
        cs = r['chosen_share']
        row = [os.path.basename(d.rstrip('/\\')), b, r['decisions'], f"{r['pass_rate']}%", r['placed_per_decision'], r['deploy_limit_mean'],
               f"{cs.get('own', 0)}/{cs.get('neutral', 0)}/{cs.get('opp', 0)}" + (f"/空区{cs['emptyzone']}" if 'emptyzone' in cs else ''),
               r['legal_empty_mean'], r['forbidden_me_mean'], f"{r['life_elim_share']}%", f"{r['over_thr_share_of_evaluated']}%",
               r['decisions_no_point_over_thr'], f"{r['greedy_kept']}/{r['greedy_wd_thr']}({r['greedy_wd_thr_positive']})/{r['greedy_wd_life']}",
               r.get('enemy_weak_stones_mean'), r.get('cap_point_decisions')]
        print('| ' + ' | '.join(str(x) for x in row) + ' |')
